using Cortex.Mediator;
using OsoujiSystem.Application.UseCases.CleaningAreas;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;
using OsoujiSystem.WebApi.Endpoints.Support;

namespace OsoujiSystem.WebApi.Endpoints.CleaningAreas;

internal static class CleaningAreaEndpoints
{
    public static IEndpointRouteBuilder MapCleaningAreaEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/cleaning-areas").WithTags("Cleaning Areas");

        group.MapGet("/", ListCleaningAreasAsync);
        group.MapGet("/{areaId:guid}", GetCleaningAreaAsync)
            .WithName("GetCleaningArea");
        group.MapPost("/", RegisterCleaningAreaAsync);
        group.MapPut("/{areaId:guid}/pending-week-rule", ScheduleWeekRuleChangeAsync);
        group.MapPost("/{areaId:guid}/spots", AddCleaningSpotAsync);
        group.MapDelete("/{areaId:guid}/spots/{spotId:guid}", RemoveCleaningSpotAsync);
        group.MapPost("/{areaId:guid}/members", AssignUserToAreaAsync);
        group.MapDelete("/{areaId:guid}/members/{userId:guid}", UnassignUserFromAreaAsync);

        api.MapPost("/area-member-transfers", TransferUserToAreaAsync)
            .WithTags("Cleaning Areas");

        return api;
    }

    private static async Task<IResult> ListCleaningAreasAsync(
        HttpRequest request,
        ICleaningAreaRepository repository,
        string? userId,
        string? cursor,
        int? limit,
        string? sort,
        CancellationToken ct)
    {
        UserId? filterUserId = null;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            if (!ApiRequestParsing.TryParseGuidId(userId, guid => new UserId(guid), out UserId parsedUserId))
            {
                return ApiHttpResults.Validation("userId", "Expected a UUID.");
            }

            filterUserId = parsedUserId;
        }

        var loaded = await repository.ListAllAsync(ct);
        var items = loaded
            .Where(x => filterUserId is null || x.Aggregate.Members.Any(member => member.UserId == filterUserId.Value));

        var ordered = (sort ?? "name").ToLowerInvariant() switch
        {
            "name" => items.OrderBy(x => x.Aggregate.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Aggregate.Id.Value),
            "-name" => items.OrderByDescending(x => x.Aggregate.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Aggregate.Id.Value),
            _ => null
        };

        if (ordered is null)
        {
            return ApiHttpResults.Validation("sort", "Supported values are name and -name.");
        }

        var pageSize = Math.Clamp(limit ?? 20, 1, 100);
        var offset = ApiRequestParsing.DecodeCursor(cursor);
        var page = ordered.Skip(offset).Take(pageSize + 1).ToArray();
        var hasNext = page.Length > pageSize;
        var data = page.Take(pageSize).Select(ToCleaningAreaSummary).ToArray();

        return TypedResults.Ok(new
        {
            data,
            meta = new
            {
                limit = pageSize,
                hasNext,
                nextCursor = hasNext ? ApiRequestParsing.EncodeCursor(offset + pageSize) : null
            },
            links = new
            {
                self = request.Path + request.QueryString.ToUriComponent()
            }
        });
    }

    private static async Task<IResult> GetCleaningAreaAsync(
        HttpResponse response,
        ICleaningAreaRepository repository,
        Guid areaId,
        CancellationToken ct)
    {
        var loaded = await repository.FindByIdAsync(new CleaningAreaId(areaId), ct);
        if (loaded is null)
        {
            return ApiHttpResults.FromError(new("NotFound", "CleaningArea was not found.", new Dictionary<string, object?>
            {
                ["resource"] = "CleaningArea",
                ["key"] = "areaId",
                ["value"] = areaId.ToString("D")
            }));
        }

        response.Headers["ETag"] = ApiHttpResults.ToEtag(loaded.Value.Version);
        return TypedResults.Ok(new { data = ToCleaningAreaDetail(loaded.Value) });
    }

    private static async Task<IResult> RegisterCleaningAreaAsync(
        HttpResponse response,
        LinkGenerator links,
        IMediator mediator,
        RegisterCleaningAreaBody body,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        if (!ApiRequestParsing.TryParseGuidId(body.AreaId, guid => new CleaningAreaId(guid), out CleaningAreaId areaId))
        {
            errors["areaId"] = ["Expected a UUID."];
        }

        if (!ApiRequestParsing.TryParseWeekRule(
            body.InitialWeekRule?.StartDay,
            body.InitialWeekRule?.StartTime,
            body.InitialWeekRule?.TimeZoneId,
            body.InitialWeekRule?.EffectiveFromWeek,
            out var initialWeekRule,
            out var weekRuleErrors))
        {
            foreach (var pair in weekRuleErrors)
            {
                errors[$"initialWeekRule.{pair.Key}"] = pair.Value;
            }
        }

        if (body.InitialSpots is null || body.InitialSpots.Count == 0)
        {
            errors["initialSpots"] = ["At least one spot is required."];
        }

        var spots = new List<RegisterCleaningSpotInput>();
        if (body.InitialSpots is not null)
        {
            foreach (var spot in body.InitialSpots.Select((value, index) => (value, index)))
            {
                if (!ApiRequestParsing.TryParseGuidId(spot.value.SpotId, guid => new CleaningSpotId(guid), out CleaningSpotId spotId))
                {
                    errors[$"initialSpots[{spot.index}].spotId"] = ["Expected a UUID."];
                    continue;
                }

                spots.Add(new RegisterCleaningSpotInput(spotId, spot.value.SpotName ?? string.Empty, spot.value.SortOrder));
            }
        }

        if (errors.Count > 0)
        {
            return ApiHttpResults.Validation(errors);
        }

        var result = await mediator.SendAsync(new RegisterCleaningAreaRequest
        {
            AreaId = areaId,
            Name = body.Name ?? string.Empty,
            InitialWeekRule = initialWeekRule,
            InitialSpots = spots
        }, ct);

        return ApiHttpResults.FromApplicationResult(result, value =>
        {
            var location = links.GetPathByName("GetCleaningArea", new { areaId = value.AreaId.Value })
                ?? $"/api/v1/cleaning-areas/{value.AreaId}";
            response.Headers["Location"] = location;
            return TypedResults.Created(location, new
            {
                data = new
                {
                    areaId = value.AreaId.ToString()
                }
            });
        });
    }

    private static async Task<IResult> ScheduleWeekRuleChangeAsync(
        HttpRequest request,
        HttpResponse response,
        ICleaningAreaRepository repository,
        IMediator mediator,
        Guid areaId,
        WeekRuleBody body,
        CancellationToken ct)
    {
        var loadResult = await LoadAreaForWriteAsync(request, repository, areaId, ct);
        if (loadResult.Result is not null)
        {
            return loadResult.Result;
        }

        if (!ApiRequestParsing.TryParseWeekRule(body.StartDay, body.StartTime, body.TimeZoneId, body.EffectiveFromWeek, out var weekRule, out var errors))
        {
            return ApiHttpResults.Validation(errors);
        }

        var result = await mediator.SendAsync(new ScheduleWeekRuleChangeRequest
        {
            AreaId = loadResult.Loaded!.Value.Aggregate.Id,
            NextWeekRule = weekRule,
            ExpectedVersion = loadResult.Loaded.Value.Version
        }, ct);

        return await ApiHttpResults.FromApplicationResultAsync(result, async _ =>
        {
            var refreshed = await repository.FindByIdAsync(loadResult.Loaded.Value.Aggregate.Id, ct);
            if (refreshed is null)
            {
                return ApiHttpResults.FromError(new("NotFound", "CleaningArea was not found.", new Dictionary<string, object?>()));
            }

            response.Headers["ETag"] = ApiHttpResults.ToEtag(refreshed.Value.Version);
            return TypedResults.Ok(new { data = ToCleaningAreaDetail(refreshed.Value) });
        });
    }

    private static async Task<IResult> AddCleaningSpotAsync(
        HttpRequest request,
        HttpResponse response,
        LinkGenerator links,
        ICleaningAreaRepository repository,
        IMediator mediator,
        Guid areaId,
        AddCleaningSpotBody body,
        CancellationToken ct)
    {
        var loadResult = await LoadAreaForWriteAsync(request, repository, areaId, ct);
        if (loadResult.Result is not null)
        {
            return loadResult.Result;
        }

        if (!ApiRequestParsing.TryParseGuidId(body.SpotId, guid => new CleaningSpotId(guid), out CleaningSpotId spotId))
        {
            return ApiHttpResults.Validation("spotId", "Expected a UUID.");
        }

        var result = await mediator.SendAsync(new AddCleaningSpotRequest
        {
            AreaId = loadResult.Loaded!.Value.Aggregate.Id,
            SpotId = spotId,
            SpotName = body.Name ?? string.Empty,
            SortOrder = body.SortOrder,
            ExpectedVersion = loadResult.Loaded.Value.Version
        }, ct);

        return await ApiHttpResults.FromApplicationResultAsync(result, async _ =>
        {
            var refreshed = await repository.FindByIdAsync(loadResult.Loaded.Value.Aggregate.Id, ct);
            if (refreshed is null)
            {
                return ApiHttpResults.FromError(new("NotFound", "CleaningArea was not found.", new Dictionary<string, object?>()));
            }

            var location = links.GetPathByName("GetCleaningArea", new { areaId })
                ?? $"/api/v1/cleaning-areas/{areaId}";
            var spotLocation = $"{location}/spots/{spotId}";
            response.Headers["Location"] = spotLocation;
            response.Headers["ETag"] = ApiHttpResults.ToEtag(refreshed.Value.Version);

            return TypedResults.Created(
                spotLocation,
                new
                {
                    data = new
                    {
                        spotId = spotId.ToString()
                    }
                });
        });
    }

    private static async Task<IResult> RemoveCleaningSpotAsync(
        HttpRequest request,
        ICleaningAreaRepository repository,
        IMediator mediator,
        Guid areaId,
        Guid spotId,
        CancellationToken ct)
    {
        var loadResult = await LoadAreaForWriteAsync(request, repository, areaId, ct);
        if (loadResult.Result is not null)
        {
            return loadResult.Result;
        }

        var result = await mediator.SendAsync(new RemoveCleaningSpotRequest
        {
            AreaId = loadResult.Loaded!.Value.Aggregate.Id,
            SpotId = new CleaningSpotId(spotId),
            ExpectedVersion = loadResult.Loaded.Value.Version
        }, ct);

        return ApiHttpResults.FromApplicationResult(result, _ => TypedResults.NoContent());
    }

    private static async Task<IResult> AssignUserToAreaAsync(
        HttpRequest request,
        HttpResponse response,
        ICleaningAreaRepository repository,
        IMediator mediator,
        Guid areaId,
        AssignUserBody body,
        CancellationToken ct)
    {
        var loadResult = await LoadAreaForWriteAsync(request, repository, areaId, ct);
        if (loadResult.Result is not null)
        {
            return loadResult.Result;
        }

        var errors = new Dictionary<string, string[]>();
        AreaMemberId? memberId = null;

        if (body.MemberId is not null)
        {
            if (!ApiRequestParsing.TryParseGuidId(body.MemberId, guid => new AreaMemberId(guid), out var parsedAreaMemberId))
            {
                errors["memberId"] = ["Expected a UUID."];
            }
            else
            {
                memberId = parsedAreaMemberId;
            }
        }

        if (!ApiRequestParsing.TryParseGuidId(body.UserId, guid => new UserId(guid), out var userId))
        {
            errors["userId"] = ["Expected a UUID."];
        }

        if (!ApiRequestParsing.TryParseEmployeeNumber(body.EmployeeNumber, out var employeeNumber, out var employeeError))
        {
            errors["employeeNumber"] = [employeeError];
        }

        if (errors.Count > 0)
        {
            return ApiHttpResults.Validation(errors);
        }

        var result = await mediator.SendAsync(new AssignUserToAreaRequest
        {
            AreaId = loadResult.Loaded!.Value.Aggregate.Id,
            AreaMemberId = memberId,
            UserId = userId,
            EmployeeNumber = employeeNumber,
            ExpectedVersion = loadResult.Loaded.Value.Version
        }, ct);

        return await ApiHttpResults.FromApplicationResultAsync(result, async _ =>
        {
            var refreshed = await repository.FindByIdAsync(loadResult.Loaded.Value.Aggregate.Id, ct);
            if (refreshed is null)
            {
                return ApiHttpResults.FromError(new("NotFound", "CleaningArea was not found.", new Dictionary<string, object?>()));
            }

            var assignedMember = refreshed.Value.Aggregate.Members.FirstOrDefault(x => x.UserId == userId);
            var memberLocation = $"/api/v1/cleaning-areas/{areaId}/members/{userId}";
            response.Headers["Location"] = memberLocation;
            response.Headers["ETag"] = ApiHttpResults.ToEtag(refreshed.Value.Version);

            return TypedResults.Created(
                memberLocation,
                new
                {
                    data = new
                    {
                        memberId = assignedMember?.Id.ToString(),
                        userId = userId.ToString()
                    }
                });
        });
    }

    private static async Task<IResult> UnassignUserFromAreaAsync(
        HttpRequest request,
        ICleaningAreaRepository repository,
        IMediator mediator,
        Guid areaId,
        Guid userId,
        CancellationToken ct)
    {
        var loadResult = await LoadAreaForWriteAsync(request, repository, areaId, ct);
        if (loadResult.Result is not null)
        {
            return loadResult.Result;
        }

        var result = await mediator.SendAsync(new UnassignUserFromAreaRequest
        {
            AreaId = loadResult.Loaded!.Value.Aggregate.Id,
            UserId = new UserId(userId),
            ExpectedVersion = loadResult.Loaded.Value.Version
        }, ct);

        return ApiHttpResults.FromApplicationResult(result, _ => TypedResults.NoContent());
    }

    private static async Task<IResult> TransferUserToAreaAsync(
        ICleaningAreaRepository repository,
        IMediator mediator,
        TransferUserBody body,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!ApiRequestParsing.TryParseGuidId(body.FromAreaId, guid => new CleaningAreaId(guid), out CleaningAreaId fromAreaId))
        {
            errors["fromAreaId"] = ["Expected a UUID."];
        }

        if (!ApiRequestParsing.TryParseGuidId(body.ToAreaId, guid => new CleaningAreaId(guid), out CleaningAreaId toAreaId))
        {
            errors["toAreaId"] = ["Expected a UUID."];
        }

        if (!ApiRequestParsing.TryParseGuidId(body.UserId, guid => new UserId(guid), out UserId userId))
        {
            errors["userId"] = ["Expected a UUID."];
        }

        if (!ApiRequestParsing.TryParseGuidId(body.ToAreaMemberId, guid => new AreaMemberId(guid), out AreaMemberId toAreaMemberId))
        {
            errors["toAreaMemberId"] = ["Expected a UUID."];
        }

        if (!ApiRequestParsing.TryParseEmployeeNumber(body.EmployeeNumber, out var employeeNumber, out var employeeError))
        {
            errors["employeeNumber"] = [employeeError];
        }

        if (body.FromAreaVersion < 1)
        {
            errors["fromAreaVersion"] = ["Expected a positive aggregate version."];
        }

        if (body.ToAreaVersion < 1)
        {
            errors["toAreaVersion"] = ["Expected a positive aggregate version."];
        }

        if (errors.Count > 0)
        {
            return ApiHttpResults.Validation(errors);
        }

        var fromLoaded = await repository.FindByIdAsync(fromAreaId, ct);
        if (fromLoaded is null)
        {
            return ApiHttpResults.FromError(new("NotFound", "CleaningArea was not found.", new Dictionary<string, object?>
            {
                ["resource"] = "CleaningArea",
                ["key"] = "fromAreaId",
                ["value"] = fromAreaId.ToString()
            }));
        }

        var toLoaded = await repository.FindByIdAsync(toAreaId, ct);
        if (toLoaded is null)
        {
            return ApiHttpResults.FromError(new("NotFound", "CleaningArea was not found.", new Dictionary<string, object?>
            {
                ["resource"] = "CleaningArea",
                ["key"] = "toAreaId",
                ["value"] = toAreaId.ToString()
            }));
        }

        if (fromLoaded.Value.Version.Value != body.FromAreaVersion)
        {
            return ApiHttpResults.FromError(new("RepositoryConcurrency", "The aggregate was updated by another transaction.", new Dictionary<string, object?>
            {
                ["resource"] = "CleaningArea",
                ["key"] = "fromAreaId",
                ["expectedVersion"] = body.FromAreaVersion,
                ["actualVersion"] = fromLoaded.Value.Version.Value
            }));
        }

        if (toLoaded.Value.Version.Value != body.ToAreaVersion)
        {
            return ApiHttpResults.FromError(new("RepositoryConcurrency", "The aggregate was updated by another transaction.", new Dictionary<string, object?>
            {
                ["resource"] = "CleaningArea",
                ["key"] = "toAreaId",
                ["expectedVersion"] = body.ToAreaVersion,
                ["actualVersion"] = toLoaded.Value.Version.Value
            }));
        }

        var result = await mediator.SendAsync(new TransferUserToAreaRequest
        {
            FromAreaId = fromAreaId,
            ToAreaId = toAreaId,
            UserId = userId,
            ToAreaMemberId = toAreaMemberId,
            EmployeeNumber = employeeNumber,
            FromExpectedVersion = fromLoaded.Value.Version,
            ToExpectedVersion = toLoaded.Value.Version
        }, ct);

        return ApiHttpResults.FromApplicationResult(result, _ => TypedResults.Ok(new
        {
            data = new
            {
                fromAreaId = fromAreaId.ToString(),
                toAreaId = toAreaId.ToString(),
                userId = userId.ToString(),
                transferred = true
            }
        }));
    }

    private static async Task<(LoadedAggregate<CleaningArea>? Loaded, IResult? Result)> LoadAreaForWriteAsync(
        HttpRequest request,
        ICleaningAreaRepository repository,
        Guid areaId,
        CancellationToken ct)
    {
        if (!ApiHttpResults.TryParseIfMatch(request, out var expectedVersion))
        {
            return (null, ApiHttpResults.Validation("If-Match", "A valid If-Match header is required."));
        }

        var loaded = await repository.FindByIdAsync(new CleaningAreaId(areaId), ct);
        if (loaded is null)
        {
            return (null, ApiHttpResults.FromError(new("NotFound", "CleaningArea was not found.", new Dictionary<string, object?>
            {
                ["resource"] = "CleaningArea",
                ["key"] = "areaId",
                ["value"] = areaId.ToString("D")
            })));
        }

        if (loaded.Value.Version != expectedVersion)
        {
            return (null, ApiHttpResults.FromError(new("RepositoryConcurrency", "The aggregate was updated by another transaction.", new Dictionary<string, object?>
            {
                ["resource"] = "CleaningArea",
                ["key"] = "areaId",
                ["expectedVersion"] = expectedVersion.Value,
                ["actualVersion"] = loaded.Value.Version.Value
            })));
        }

        return (loaded, null);
    }

    private static object ToCleaningAreaSummary(LoadedAggregate<CleaningArea> loaded) => new
    {
        id = loaded.Aggregate.Id.ToString(),
        name = loaded.Aggregate.Name,
        currentWeekRule = ToWeekRule(loaded.Aggregate.CurrentWeekRule),
        memberCount = loaded.Aggregate.Members.Count,
        spotCount = loaded.Aggregate.Spots.Count,
        version = loaded.Version.Value
    };

    private static object ToCleaningAreaDetail(LoadedAggregate<CleaningArea> loaded) => new
    {
        id = loaded.Aggregate.Id.ToString(),
        name = loaded.Aggregate.Name,
        currentWeekRule = ToWeekRule(loaded.Aggregate.CurrentWeekRule),
        pendingWeekRule = loaded.Aggregate.PendingWeekRule is null ? null : ToWeekRule(loaded.Aggregate.PendingWeekRule.Value),
        rotationCursor = loaded.Aggregate.RotationCursor.Value,
        spots = loaded.Aggregate.Spots.Select(spot => new
        {
            id = spot.Id.ToString(),
            name = spot.Name,
            sortOrder = spot.SortOrder
        }),
        members = loaded.Aggregate.Members.Select(member => new
        {
            id = member.Id.ToString(),
            userId = member.UserId.ToString(),
            employeeNumber = member.EmployeeNumber.Value
        }),
        version = loaded.Version.Value
    };

    private static object ToWeekRule(WeekRule rule) => new
    {
        startDay = ApiRequestParsing.ToApiDayOfWeek(rule.StartDay),
        startTime = rule.StartTime.ToString("HH:mm:ss"),
        timeZoneId = rule.TimeZoneId,
        effectiveFromWeek = rule.EffectiveFromWeek.ToString()
    };

    private sealed record RegisterCleaningAreaBody(
        string? AreaId,
        string? Name,
        WeekRuleBody? InitialWeekRule,
        IReadOnlyList<RegisterCleaningSpotBody>? InitialSpots);

    private sealed record RegisterCleaningSpotBody(
        string? SpotId,
        string? SpotName,
        int SortOrder);

    private sealed record WeekRuleBody(
        string? StartDay,
        string? StartTime,
        string? TimeZoneId,
        string? EffectiveFromWeek);

    private sealed record AddCleaningSpotBody(
        string? SpotId,
        string? Name,
        int SortOrder);

    private sealed record AssignUserBody(
        string? MemberId,
        string? UserId,
        string? EmployeeNumber);

    private sealed record TransferUserBody(
        string? FromAreaId,
        string? ToAreaId,
        string? UserId,
        string? ToAreaMemberId,
        string? EmployeeNumber,
        long FromAreaVersion,
        long ToAreaVersion);
}

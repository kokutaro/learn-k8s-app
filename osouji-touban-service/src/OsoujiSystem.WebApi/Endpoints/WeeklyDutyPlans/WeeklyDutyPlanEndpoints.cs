using Cortex.Mediator;
using OsoujiSystem.Application.UseCases.WeeklyDutyPlans;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;
using OsoujiSystem.WebApi.Endpoints.Support;

namespace OsoujiSystem.WebApi.Endpoints.WeeklyDutyPlans;

internal static class WeeklyDutyPlanEndpoints
{
    public static IEndpointRouteBuilder MapWeeklyDutyPlanEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/weekly-duty-plans").WithTags("Weekly Duty Plans");

        group.MapGet("/", ListWeeklyDutyPlansAsync);
        group.MapGet("/{planId:guid}", GetWeeklyDutyPlanAsync)
            .WithName("GetWeeklyDutyPlan");
        group.MapPost("/", GenerateWeeklyPlanAsync);
        group.MapPut("/{planId:guid}/publication", PublishWeeklyPlanAsync);
        group.MapPut("/{planId:guid}/closure", CloseWeeklyPlanAsync);

        return api;
    }

    private static async Task<IResult> ListWeeklyDutyPlansAsync(
        HttpRequest request,
        IWeeklyDutyPlanRepository repository,
        string? areaId,
        string? weekId,
        string? status,
        string? cursor,
        int? limit,
        string? sort,
        CancellationToken ct)
    {
        CleaningAreaId? areaFilter = null;
        WeekId? weekFilter = null;
        WeeklyPlanStatus? statusFilter = null;
        var errors = new Dictionary<string, string[]>();

        if (!string.IsNullOrWhiteSpace(areaId))
        {
            if (!ApiRequestParsing.TryParseGuidId(areaId, guid => new CleaningAreaId(guid), out CleaningAreaId parsedAreaId))
            {
                errors["areaId"] = ["Expected a UUID."];
            }
            else
            {
                areaFilter = parsedAreaId;
            }
        }

        if (!string.IsNullOrWhiteSpace(weekId))
        {
            if (!ApiRequestParsing.TryParseWeekId(weekId, out var parsedWeekId, out var weekError))
            {
                errors["weekId"] = [weekError?.Message ?? "Invalid weekId."];
            }
            else
            {
                weekFilter = parsedWeekId;
            }
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!ApiRequestParsing.TryParseWeeklyPlanStatus(status, out WeeklyPlanStatus parsedStatus))
            {
                errors["status"] = ["Supported values are draft, published, closed."];
            }
            else
            {
                statusFilter = parsedStatus;
            }
        }

        if (errors.Count > 0)
        {
            return ApiHttpResults.Validation(errors);
        }

        var loaded = await repository.ListAsync(areaFilter, weekFilter, statusFilter, ct);
        var ordered = (sort ?? "-weekId").ToLowerInvariant() switch
        {
            "weekid" => loaded.OrderBy(x => x.Aggregate.WeekId.Year).ThenBy(x => x.Aggregate.WeekId.WeekNumber).ThenBy(x => x.Aggregate.Id.Value),
            "-weekid" => loaded.OrderByDescending(x => x.Aggregate.WeekId.Year).ThenByDescending(x => x.Aggregate.WeekId.WeekNumber).ThenBy(x => x.Aggregate.Id.Value),
            _ => null
        };

        if (ordered is null)
        {
            return ApiHttpResults.Validation("sort", "Supported values are weekId and -weekId.");
        }

        var pageSize = Math.Clamp(limit ?? 20, 1, 100);
        var offset = ApiRequestParsing.DecodeCursor(cursor);
        var page = ordered.Skip(offset).Take(pageSize + 1).ToArray();
        var hasNext = page.Length > pageSize;

        return TypedResults.Ok(new
        {
            data = page.Take(pageSize).Select(ToWeeklyDutyPlanSummary).ToArray(),
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

    private static async Task<IResult> GetWeeklyDutyPlanAsync(
        HttpResponse response,
        IWeeklyDutyPlanRepository repository,
        Guid planId,
        CancellationToken ct)
    {
        var loaded = await repository.FindByIdAsync(new WeeklyDutyPlanId(planId), ct);
        if (loaded is null)
        {
            return ApiHttpResults.FromError(new("NotFound", "WeeklyDutyPlan was not found.", new Dictionary<string, object?>
            {
                ["resource"] = "WeeklyDutyPlan",
                ["key"] = "planId",
                ["value"] = planId.ToString("D")
            }));
        }

        response.Headers["ETag"] = ApiHttpResults.ToEtag(loaded.Value.Version);
        return TypedResults.Ok(new { data = ToWeeklyDutyPlanDetail(loaded.Value) });
    }

    private static async Task<IResult> GenerateWeeklyPlanAsync(
        HttpResponse response,
        LinkGenerator links,
        IMediator mediator,
        GenerateWeeklyPlanBody body,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        if (!ApiRequestParsing.TryParseGuidId(body.AreaId, guid => new CleaningAreaId(guid), out CleaningAreaId areaId))
        {
            errors["areaId"] = ["Expected a UUID."];
        }

        if (!ApiRequestParsing.TryParseWeekId(body.WeekId, out var weekId, out var weekError))
        {
            errors["weekId"] = [weekError?.Message ?? "Invalid weekId."];
        }

        if (body.Policy?.FairnessWindowWeeks is <= 0)
        {
            errors["policy.fairnessWindowWeeks"] = ["Expected a positive integer."];
        }

        if (errors.Count > 0)
        {
            return ApiHttpResults.Validation(errors);
        }

        var result = await mediator.SendAsync(new GenerateWeeklyPlanRequest
        {
            AreaId = areaId,
            WeekId = weekId,
            Policy = new AssignmentPolicy(body.Policy?.FairnessWindowWeeks ?? AssignmentPolicy.Default.FairnessWindowWeeks)
        }, ct);

        return ApiHttpResults.FromApplicationResult(result, value =>
        {
            var location = links.GetPathByName("GetWeeklyDutyPlan", new { planId = value.PlanId.Value })
                ?? $"/api/v1/weekly-duty-plans/{value.PlanId}";
            response.Headers["Location"] = location;

            return TypedResults.Created(location, new
            {
                data = new
                {
                    planId = value.PlanId.ToString(),
                    weekId = value.WeekId.ToString(),
                    revision = value.Revision.Value,
                    status = ApiRequestParsing.ToApiStatus(value.Status)
                }
            });
        });
    }

    private static async Task<IResult> PublishWeeklyPlanAsync(
        HttpRequest request,
        HttpResponse response,
        IWeeklyDutyPlanRepository repository,
        IMediator mediator,
        Guid planId,
        CancellationToken ct)
    {
        var loadResult = await LoadPlanForWriteAsync(request, repository, planId, ct);
        if (loadResult.Result is not null)
        {
            return loadResult.Result;
        }

        var result = await mediator.SendAsync(new PublishWeeklyPlanRequest
        {
            PlanId = loadResult.Loaded!.Value.Aggregate.Id,
            ExpectedVersion = loadResult.Loaded.Value.Version
        }, ct);

        return await ApiHttpResults.FromApplicationResultAsync(result, async _ =>
        {
            var refreshed = await repository.FindByIdAsync(loadResult.Loaded.Value.Aggregate.Id, ct);
            if (refreshed is null)
            {
                return ApiHttpResults.FromError(new("NotFound", "WeeklyDutyPlan was not found.", new Dictionary<string, object?>()));
            }

            response.Headers["ETag"] = ApiHttpResults.ToEtag(refreshed.Value.Version);
            return TypedResults.Ok(new
            {
                data = new
                {
                    planId = refreshed.Value.Aggregate.Id.ToString(),
                    status = ApiRequestParsing.ToApiStatus(refreshed.Value.Aggregate.Status)
                }
            });
        });
    }

    private static async Task<IResult> CloseWeeklyPlanAsync(
        HttpRequest request,
        HttpResponse response,
        IWeeklyDutyPlanRepository repository,
        IMediator mediator,
        Guid planId,
        CancellationToken ct)
    {
        var loadResult = await LoadPlanForWriteAsync(request, repository, planId, ct);
        if (loadResult.Result is not null)
        {
            return loadResult.Result;
        }

        var result = await mediator.SendAsync(new CloseWeeklyPlanRequest
        {
            PlanId = loadResult.Loaded!.Value.Aggregate.Id,
            ExpectedVersion = loadResult.Loaded.Value.Version
        }, ct);

        return await ApiHttpResults.FromApplicationResultAsync(result, async _ =>
        {
            var refreshed = await repository.FindByIdAsync(loadResult.Loaded.Value.Aggregate.Id, ct);
            if (refreshed is null)
            {
                return ApiHttpResults.FromError(new("NotFound", "WeeklyDutyPlan was not found.", new Dictionary<string, object?>()));
            }

            response.Headers["ETag"] = ApiHttpResults.ToEtag(refreshed.Value.Version);
            return TypedResults.Ok(new
            {
                data = new
                {
                    planId = refreshed.Value.Aggregate.Id.ToString(),
                    status = ApiRequestParsing.ToApiStatus(refreshed.Value.Aggregate.Status)
                }
            });
        });
    }

    private static async Task<(LoadedAggregate<WeeklyDutyPlan>? Loaded, IResult? Result)> LoadPlanForWriteAsync(
        HttpRequest request,
        IWeeklyDutyPlanRepository repository,
        Guid planId,
        CancellationToken ct)
    {
        if (!ApiHttpResults.TryParseIfMatch(request, out var expectedVersion))
        {
            return (null, ApiHttpResults.Validation("If-Match", "A valid If-Match header is required."));
        }

        var loaded = await repository.FindByIdAsync(new WeeklyDutyPlanId(planId), ct);
        if (loaded is null)
        {
            return (null, ApiHttpResults.FromError(new("NotFound", "WeeklyDutyPlan was not found.", new Dictionary<string, object?>
            {
                ["resource"] = "WeeklyDutyPlan",
                ["key"] = "planId",
                ["value"] = planId.ToString("D")
            })));
        }

        if (loaded.Value.Version != expectedVersion)
        {
            return (null, ApiHttpResults.FromError(new("RepositoryConcurrency", "The aggregate was updated by another transaction.", new Dictionary<string, object?>
            {
                ["resource"] = "WeeklyDutyPlan",
                ["key"] = "planId",
                ["expectedVersion"] = expectedVersion.Value,
                ["actualVersion"] = loaded.Value.Version.Value
            })));
        }

        return (loaded, null);
    }

    private static object ToWeeklyDutyPlanSummary(LoadedAggregate<WeeklyDutyPlan> loaded) => new
    {
        id = loaded.Aggregate.Id.ToString(),
        areaId = loaded.Aggregate.AreaId.ToString(),
        weekId = loaded.Aggregate.WeekId.ToString(),
        revision = loaded.Aggregate.Revision.Value,
        status = ApiRequestParsing.ToApiStatus(loaded.Aggregate.Status),
        version = loaded.Version.Value
    };

    private static object ToWeeklyDutyPlanDetail(LoadedAggregate<WeeklyDutyPlan> loaded) => new
    {
        id = loaded.Aggregate.Id.ToString(),
        areaId = loaded.Aggregate.AreaId.ToString(),
        weekId = loaded.Aggregate.WeekId.ToString(),
        revision = loaded.Aggregate.Revision.Value,
        status = ApiRequestParsing.ToApiStatus(loaded.Aggregate.Status),
        assignmentPolicy = new
        {
            fairnessWindowWeeks = loaded.Aggregate.AssignmentPolicy.FairnessWindowWeeks
        },
        assignments = loaded.Aggregate.Assignments.Select(x => new
        {
            spotId = x.SpotId.ToString(),
            userId = x.UserId.ToString()
        }),
        offDutyEntries = loaded.Aggregate.OffDutyEntries.Select(x => new
        {
            userId = x.UserId.ToString()
        }),
        version = loaded.Version.Value
    };

    private sealed record GenerateWeeklyPlanBody(
        string? AreaId,
        string? WeekId,
        AssignmentPolicyBody? Policy);

    private sealed record AssignmentPolicyBody(int FairnessWindowWeeks);
}

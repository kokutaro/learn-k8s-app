using Cortex.Mediator;
using OsoujiSystem.Application.Queries.WeeklyDutyPlans;
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
        IMediator mediator,
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

        var sortOrder = (sort ?? "-weekId").ToLowerInvariant() switch
        {
            "weekid" => WeeklyDutyPlanSortOrder.WeekIdAsc,
            "-weekid" => WeeklyDutyPlanSortOrder.WeekIdDesc,
            "createdat" => WeeklyDutyPlanSortOrder.CreatedAtAsc,
            "-createdat" => WeeklyDutyPlanSortOrder.CreatedAtDesc,
            _ => (WeeklyDutyPlanSortOrder?)null
        };

        if (sortOrder is null)
        {
            return ApiHttpResults.Validation("sort", "Supported values are weekId, -weekId, createdAt and -createdAt.");
        }

        var pageSize = Math.Clamp(limit ?? 20, 1, 100);
        var page = await mediator.QueryAsync(
            new ListWeeklyDutyPlansQuery(
                areaFilter?.Value,
                weekFilter,
                statusFilter,
                cursor,
                pageSize,
                sortOrder.Value),
            ct);

        return TypedResults.Ok(new
        {
            data = page.Items.Select(ToWeeklyDutyPlanSummary).ToArray(),
            meta = new
            {
                limit = page.Limit,
                hasNext = page.HasNext,
                nextCursor = page.NextCursor
            },
            links = new
            {
                self = request.Path + request.QueryString.ToUriComponent()
            }
        });
    }

    private static async Task<IResult> GetWeeklyDutyPlanAsync(
        HttpResponse response,
        IMediator mediator,
        Guid planId,
        CancellationToken ct)
    {
        var plan = await mediator.QueryAsync(new GetWeeklyDutyPlanQuery(planId), ct);
        if (plan is null)
        {
            return ApiHttpResults.FromError(new("NotFound", "WeeklyDutyPlan was not found.", new Dictionary<string, object?>
            {
                ["resource"] = "WeeklyDutyPlan",
                ["key"] = "planId",
                ["value"] = planId.ToString("D")
            }));
        }

        response.Headers["ETag"] = ApiHttpResults.ToEtag(new AggregateVersion(plan.Version));
        return TypedResults.Ok(new { data = ToWeeklyDutyPlanDetail(plan) });
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

    private static object ToWeeklyDutyPlanSummary(WeeklyDutyPlanListItemReadModel plan) => new
    {
        id = plan.Id.ToString(),
        areaId = plan.AreaId.ToString(),
        weekId = plan.WeekId,
        revision = plan.Revision,
        status = plan.Status,
        version = plan.Version
    };

    private static object ToWeeklyDutyPlanDetail(WeeklyDutyPlanDetailReadModel plan) => new
    {
        id = plan.Id.ToString(),
        areaId = plan.AreaId.ToString(),
        weekId = plan.WeekId,
        revision = plan.Revision,
        status = plan.Status,
        assignmentPolicy = new
        {
            fairnessWindowWeeks = plan.AssignmentPolicy.FairnessWindowWeeks
        },
        assignments = plan.Assignments.Select(x => new
        {
            spotId = x.SpotId.ToString(),
            userId = x.UserId.ToString(),
            user = x.User is null ? null : new
            {
                userId = x.User.UserId.ToString(),
                employeeNumber = x.User.EmployeeNumber,
                displayName = x.User.DisplayName,
                departmentCode = x.User.DepartmentCode,
                lifecycleStatus = x.User.LifecycleStatus
            }
        }),
        offDutyEntries = plan.OffDutyEntries.Select(x => new
        {
            userId = x.UserId.ToString(),
            user = x.User is null ? null : new
            {
                userId = x.User.UserId.ToString(),
                employeeNumber = x.User.EmployeeNumber,
                displayName = x.User.DisplayName,
                departmentCode = x.User.DepartmentCode,
                lifecycleStatus = x.User.LifecycleStatus
            }
        }),
        version = plan.Version
    };

    private sealed record GenerateWeeklyPlanBody(
        string? AreaId,
        string? WeekId,
        AssignmentPolicyBody? Policy);

    private sealed record AssignmentPolicyBody(int FairnessWindowWeeks);
}

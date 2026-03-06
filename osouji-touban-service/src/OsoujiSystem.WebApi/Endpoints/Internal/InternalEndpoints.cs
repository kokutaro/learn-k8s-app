using Cortex.Mediator;
using OsoujiSystem.Application.UseCases.Batches;
using OsoujiSystem.Application.UseCases.CleaningAreas;
using OsoujiSystem.Domain.ValueObjects;
using OsoujiSystem.WebApi.Endpoints.Support;

namespace OsoujiSystem.WebApi.Endpoints.Internal;

internal static class InternalEndpoints
{
    public static IEndpointRouteBuilder MapInternalEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/internal").WithTags("Internal");

        group.MapPost("/week-rule-applications", ApplyDueWeekRuleChangesAsync);
        group.MapPost("/current-week-plan-generations", GenerateCurrentWeekPlansBatchAsync);

        return api;
    }

    private static async Task<IResult> ApplyDueWeekRuleChangesAsync(
        IMediator mediator,
        ApplyDueWeekRuleChangesBody? body,
        CancellationToken ct)
    {
        WeekId? currentWeek = null;
        if (body?.CurrentWeek is not null
            && !ApiRequestParsing.TryParseWeekId(body.CurrentWeek, out _, out var error))
        {
            return ApiHttpResults.Validation("currentWeek", error?.Message ?? "Invalid weekId.");
        }

        if (body?.CurrentWeek is not null)
        {
            _ = ApiRequestParsing.TryParseWeekId(body.CurrentWeek, out var parsedWeekId, out _);
            currentWeek = parsedWeekId;
        }

        var result = await mediator.SendAsync(new ApplyDueWeekRuleChangesRequest
        {
            CurrentWeek = currentWeek
        }, ct);

        return ApiHttpResults.FromApplicationResult(result, value => TypedResults.Ok(new
        {
            data = new
            {
                appliedCount = value.AppliedCount
            }
        }));
    }

    private static async Task<IResult> GenerateCurrentWeekPlansBatchAsync(
        IMediator mediator,
        GenerateCurrentWeekPlansBatchBody? body,
        CancellationToken ct)
    {
        if (body?.Policy?.FairnessWindowWeeks is <= 0)
        {
            return ApiHttpResults.Validation("policy.fairnessWindowWeeks", "Expected a positive integer.");
        }

        var result = await mediator.SendAsync(new GenerateCurrentWeekPlansBatchRequest
        {
            Policy = new(body?.Policy?.FairnessWindowWeeks ?? 4)
        }, ct);

        return ApiHttpResults.FromApplicationResult(result, value => TypedResults.Ok(new
        {
            data = new
            {
                generatedCount = value.GeneratedCount,
                skippedCount = value.SkippedCount,
                failedCount = value.FailedCount
            }
        }));
    }

    private sealed record ApplyDueWeekRuleChangesBody(string? CurrentWeek);

    private sealed record GenerateCurrentWeekPlansBatchBody(AssignmentPolicyBody? Policy);

    private sealed record AssignmentPolicyBody(int FairnessWindowWeeks);
}

using Microsoft.Extensions.Logging;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.UseCases.WeeklyDutyPlans;

public sealed class AutoSchedulePlanService(
    ICleaningAreaRepository cleaningAreaRepository,
    IWeeklyDutyPlanRepository weeklyDutyPlanRepository,
    PlanComputationService planComputationService,
    IIdGenerator idGenerator,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher,
    IClock clock,
    ILogger<AutoSchedulePlanService> logger)
{
    public async Task ProcessAllAreasAsync(CancellationToken ct)
    {
        var allAreas = await cleaningAreaRepository.ListAllAsync(ct);

        foreach (var loaded in allAreas)
        {
            try
            {
                await ProcessAreaAsync(loaded, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-schedule failed for area {AreaId}.", loaded.Aggregate.Id);
            }
        }
    }

    internal async Task<AutoScheduleAreaResult> ProcessAreaAsync(
        LoadedAggregate<CleaningArea> loaded,
        CancellationToken ct)
    {
        var area = loaded.Aggregate;

        if (area.Members.Count == 0 || area.Spots.Count == 0)
        {
            logger.LogDebug(
                "Skipping auto-schedule for area {AreaId}: no members or no spots.",
                area.Id);
            return AutoScheduleAreaResult.Skipped;
        }

        var currentWeekId = ComputeCurrentWeekId(area.CurrentWeekRule, clock.UtcNow);
        if (!HasWeekStarted(area.CurrentWeekRule, currentWeekId, clock.UtcNow))
        {
            return AutoScheduleAreaResult.Skipped;
        }

        var existingPlan = await weeklyDutyPlanRepository.FindByAreaAndWeekAsync(area.Id, currentWeekId, ct);
        if (existingPlan is not null)
        {
            if (existingPlan.Value.Aggregate.Status == WeeklyPlanStatus.Draft)
            {
                return await PublishExistingDraftAsync(existingPlan.Value, ct);
            }

            return AutoScheduleAreaResult.Skipped;
        }

        return await GenerateAndPublishAsync(loaded, currentWeekId, ct);
    }

    private async Task<AutoScheduleAreaResult> GenerateAndPublishAsync(
        LoadedAggregate<CleaningArea> loaded,
        WeekId weekId,
        CancellationToken ct)
    {
        return await transaction.ExecuteAsync(
            async token =>
            {
                var area = loaded.Aggregate;
                var policy = AssignmentPolicy.Default;

                area.ApplyPendingWeekRule(weekId);

                var computeResult = await planComputationService.ComputeInitialAsync(area, weekId, policy, token);
                if (computeResult.IsFailure)
                {
                    logger.LogWarning(
                        "Auto-schedule computation failed for area {AreaId}, week {WeekId}: {Error}",
                        area.Id, weekId, computeResult.Error);
                    return AutoScheduleAreaResult.Failed;
                }

                var planId = idGenerator.NewWeeklyDutyPlanId();
                var generatedResult = WeeklyDutyPlan.Generate(
                    planId,
                    area.Id,
                    weekId,
                    policy,
                    computeResult.Value.Assignments,
                    computeResult.Value.OffDutyEntries);

                if (generatedResult.IsFailure)
                {
                    logger.LogWarning(
                        "Auto-schedule generation failed for area {AreaId}, week {WeekId}: {Error}",
                        area.Id, weekId, generatedResult.Error);
                    return AutoScheduleAreaResult.Failed;
                }

                var plan = generatedResult.Value;
                area.UpdateRotationCursor(computeResult.Value.NextRotationCursor);

                var publishResult = plan.Publish();
                if (publishResult.IsFailure)
                {
                    logger.LogWarning(
                        "Auto-schedule publish failed for area {AreaId}, week {WeekId}: {Error}",
                        area.Id, weekId, publishResult.Error);
                    return AutoScheduleAreaResult.Failed;
                }

                await weeklyDutyPlanRepository.AddAsync(plan, token);
                await cleaningAreaRepository.SaveAsync(area, loaded.Version, token);

                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, area, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, plan, token);

                logger.LogInformation(
                    "Auto-scheduled plan {PlanId} for area {AreaId}, week {WeekId}.",
                    plan.Id, area.Id, weekId);

                return AutoScheduleAreaResult.GeneratedAndPublished;
            },
            ct);
    }

    private async Task<AutoScheduleAreaResult> PublishExistingDraftAsync(
        LoadedAggregate<WeeklyDutyPlan> loaded,
        CancellationToken ct)
    {
        return await transaction.ExecuteAsync(
            async token =>
            {
                var plan = loaded.Aggregate;
                var result = plan.Publish();
                if (result.IsFailure)
                {
                    logger.LogWarning(
                        "Auto-publish draft failed for plan {PlanId}: {Error}",
                        plan.Id, result.Error);
                    return AutoScheduleAreaResult.Failed;
                }

                await weeklyDutyPlanRepository.SaveAsync(plan, loaded.Version, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, plan, token);

                logger.LogInformation(
                    "Auto-published existing draft plan {PlanId} for area {AreaId}, week {WeekId}.",
                    plan.Id, plan.AreaId, plan.WeekId);

                return AutoScheduleAreaResult.Published;
            },
            ct);
    }

    internal static WeekId ComputeCurrentWeekId(WeekRule weekRule, DateTimeOffset utcNow)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(weekRule.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(utcNow, tz);
        return WeekId.FromDate(DateOnly.FromDateTime(localNow.DateTime));
    }

    internal static bool HasWeekStarted(WeekRule weekRule, WeekId weekId, DateTimeOffset utcNow)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(weekRule.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(utcNow, tz);

        var weekStartDate = weekId.GetStartDate(weekRule.StartDay);
        var weekStartLocal = weekStartDate.ToDateTime(weekRule.StartTime);

        return localNow.DateTime >= weekStartLocal;
    }
}

public enum AutoScheduleAreaResult
{
    Skipped,
    GeneratedAndPublished,
    Published,
    Failed
}

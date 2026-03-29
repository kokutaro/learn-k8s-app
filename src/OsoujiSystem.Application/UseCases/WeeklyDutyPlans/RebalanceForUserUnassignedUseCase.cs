using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.WeeklyDutyPlans;

public sealed record RebalanceForUserUnassignedRequest : ICommand<ApplicationResult<DomainUnit>>
{
    public required WeeklyDutyPlanId PlanId { get; init; }
    public required UserId RemovedUserId { get; init; }
}

public sealed class RebalanceForUserUnassignedUseCase(
    IWeeklyDutyPlanRepository weeklyDutyPlanRepository,
    ICleaningAreaRepository cleaningAreaRepository,
    PlanComputationService planComputationService,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher)
    : ICommandHandler<RebalanceForUserUnassignedRequest, ApplicationResult<DomainUnit>>
{
    public Task<ApplicationResult<DomainUnit>> Handle(RebalanceForUserUnassignedRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                var planLoaded = await weeklyDutyPlanRepository.FindByIdAsync(request.PlanId, token);
                if (planLoaded is null)
                {
                    return NotFoundErrors.Create<DomainUnit>("WeeklyDutyPlan", "planId", request.PlanId.ToString());
                }

                var areaLoaded = await cleaningAreaRepository.FindByIdAsync(planLoaded.Value.Aggregate.AreaId, token);
                if (areaLoaded is null)
                {
                    return NotFoundErrors.Create<DomainUnit>("CleaningArea", "areaId", planLoaded.Value.Aggregate.AreaId.ToString());
                }

                var plan = planLoaded.Value.Aggregate;
                var area = areaLoaded.Value.Aggregate;

                var engineResult = await planComputationService.RebalanceForUserUnassignedAsync(area, plan, request.RemovedUserId, token);
                if (engineResult.IsFailure)
                {
                    return ApplicationResult<DomainUnit>.FromDomainError(engineResult.Error);
                }

                var rebalanceResult = plan.RebalanceForUserUnassigned(
                    request.RemovedUserId,
                    engineResult.Value.Assignments,
                    engineResult.Value.OffDutyEntries);

                if (rebalanceResult.IsFailure)
                {
                    return ApplicationResult<DomainUnit>.FromDomainError(rebalanceResult.Error);
                }

                area.UpdateRotationCursor(engineResult.Value.NextRotationCursor);

                await weeklyDutyPlanRepository.SaveAsync(plan, planLoaded.Value.Version, token);
                await cleaningAreaRepository.SaveAsync(area, areaLoaded.Value.Version, token);

                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, area, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, plan, token);
                return ApplicationResult<DomainUnit>.Success(DomainUnit.Value);
            },
            ct);
    }
}

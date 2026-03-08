using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.WeeklyDutyPlans;

public sealed record RecalculateForSpotChangedRequest : ICommand<ApplicationResult<DomainUnit>>
{
    public required WeeklyDutyPlanId PlanId { get; init; }
}

public sealed class RecalculateForSpotChangedUseCase(
    IWeeklyDutyPlanRepository weeklyDutyPlanRepository,
    ICleaningAreaRepository cleaningAreaRepository,
    PlanComputationService planComputationService,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher)
    : ICommandHandler<RecalculateForSpotChangedRequest, ApplicationResult<DomainUnit>>
{
    public Task<ApplicationResult<DomainUnit>> Handle(RecalculateForSpotChangedRequest request, CancellationToken ct)
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

                var engineResult = await planComputationService.RecalculateForSpotChangedAsync(area, plan, token);
                if (engineResult.IsFailure)
                {
                    return ApplicationResult<DomainUnit>.FromDomainError(engineResult.Error);
                }

                var recalcResult = plan.RecalculateForSpotChanged(
                    engineResult.Value.Assignments,
                    engineResult.Value.OffDutyEntries);

                if (recalcResult.IsFailure)
                {
                    return ApplicationResult<DomainUnit>.FromDomainError(recalcResult.Error);
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

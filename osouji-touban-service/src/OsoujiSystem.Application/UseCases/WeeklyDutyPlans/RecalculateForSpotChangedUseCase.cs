using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.WeeklyDutyPlans;

public sealed record RecalculateForSpotChangedRequest : IRequest<ApplicationResult<DomainUnit>>
{
    public required WeeklyDutyPlanId PlanId { get; init; }
}

public sealed class RecalculateForSpotChangedUseCase
    : IRequestHandler<RecalculateForSpotChangedRequest, ApplicationResult<DomainUnit>>
{
    private readonly IWeeklyDutyPlanRepository _weeklyDutyPlanRepository;
    private readonly ICleaningAreaRepository _cleaningAreaRepository;
    private readonly PlanComputationService _planComputationService;
    private readonly IApplicationTransaction _transaction;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public RecalculateForSpotChangedUseCase(
        IWeeklyDutyPlanRepository weeklyDutyPlanRepository,
        ICleaningAreaRepository cleaningAreaRepository,
        PlanComputationService planComputationService,
        IApplicationTransaction transaction,
        IDomainEventDispatcher domainEventDispatcher)
    {
        _weeklyDutyPlanRepository = weeklyDutyPlanRepository;
        _cleaningAreaRepository = cleaningAreaRepository;
        _planComputationService = planComputationService;
        _transaction = transaction;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public Task<ApplicationResult<DomainUnit>> Handle(RecalculateForSpotChangedRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            _transaction,
            async token =>
            {
                var planLoaded = await _weeklyDutyPlanRepository.FindByIdAsync(request.PlanId, token);
                if (planLoaded is null)
                {
                    return NotFoundErrors.Create<DomainUnit>("WeeklyDutyPlan", "planId", request.PlanId.ToString());
                }

                var areaLoaded = await _cleaningAreaRepository.FindByIdAsync(planLoaded.Value.Aggregate.AreaId, token);
                if (areaLoaded is null)
                {
                    return NotFoundErrors.Create<DomainUnit>("CleaningArea", "areaId", planLoaded.Value.Aggregate.AreaId.ToString());
                }

                var plan = planLoaded.Value.Aggregate;
                var area = areaLoaded.Value.Aggregate;

                var engineResult = await _planComputationService.RecalculateForSpotChangedAsync(area, plan, token);
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

                await _weeklyDutyPlanRepository.SaveAsync(plan, planLoaded.Value.Version, token);
                await _cleaningAreaRepository.SaveAsync(area, areaLoaded.Value.Version, token);

                await UseCaseExecution.DispatchAndClearAsync(_domainEventDispatcher, area, token);
                await UseCaseExecution.DispatchAndClearAsync(_domainEventDispatcher, plan, token);
                return ApplicationResult<DomainUnit>.Success(DomainUnit.Value);
            },
            ct);
    }
}

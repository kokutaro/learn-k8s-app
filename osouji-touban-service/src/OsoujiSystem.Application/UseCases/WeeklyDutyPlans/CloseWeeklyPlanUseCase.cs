using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.WeeklyDutyPlans;

public sealed record CloseWeeklyPlanRequest : IRequest<ApplicationResult<DomainUnit>>
{
    public required WeeklyDutyPlanId PlanId { get; init; }
}

public sealed class CloseWeeklyPlanUseCase : IRequestHandler<CloseWeeklyPlanRequest, ApplicationResult<DomainUnit>>
{
    private readonly IWeeklyDutyPlanRepository _weeklyDutyPlanRepository;
    private readonly IApplicationTransaction _transaction;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public CloseWeeklyPlanUseCase(
        IWeeklyDutyPlanRepository weeklyDutyPlanRepository,
        IApplicationTransaction transaction,
        IDomainEventDispatcher domainEventDispatcher)
    {
        _weeklyDutyPlanRepository = weeklyDutyPlanRepository;
        _transaction = transaction;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public Task<ApplicationResult<DomainUnit>> Handle(CloseWeeklyPlanRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            _transaction,
            async token =>
            {
                var loaded = await _weeklyDutyPlanRepository.FindByIdAsync(request.PlanId, token);
                if (loaded is null)
                {
                    return NotFoundErrors.Create<DomainUnit>("WeeklyDutyPlan", "planId", request.PlanId.ToString());
                }

                var plan = loaded.Value.Aggregate;
                var beforeEvents = plan.DomainEvents.Count;
                var result = plan.Close();

                if (result.IsFailure)
                {
                    return ApplicationResult<DomainUnit>.FromDomainError(result.Error);
                }

                if (plan.DomainEvents.Count == beforeEvents)
                {
                    return ApplicationResult<DomainUnit>.Success(DomainUnit.Value);
                }

                await _weeklyDutyPlanRepository.SaveAsync(plan, loaded.Value.Version, token);
                await UseCaseExecution.DispatchAndClearAsync(_domainEventDispatcher, plan, token);
                return ApplicationResult<DomainUnit>.Success(DomainUnit.Value);
            },
            ct);
    }
}

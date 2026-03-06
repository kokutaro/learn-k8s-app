using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.WeeklyDutyPlans;

public sealed record PublishWeeklyPlanRequest : ICommand<ApplicationResult<DomainUnit>>
{
    public required WeeklyDutyPlanId PlanId { get; init; }
    public AggregateVersion? ExpectedVersion { get; init; }
}

public sealed class PublishWeeklyPlanUseCase(
    IWeeklyDutyPlanRepository weeklyDutyPlanRepository,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher)
    : ICommandHandler<PublishWeeklyPlanRequest, ApplicationResult<DomainUnit>>
{
    public Task<ApplicationResult<DomainUnit>> Handle(PublishWeeklyPlanRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                var loaded = await weeklyDutyPlanRepository.FindByIdAsync(request.PlanId, token);
                if (loaded is null)
                {
                    return NotFoundErrors.Create<DomainUnit>("WeeklyDutyPlan", "planId", request.PlanId.ToString());
                }

                var plan = loaded.Value.Aggregate;
                var result = plan.Publish();
                if (result.IsFailure)
                {
                    return ApplicationResult<DomainUnit>.FromDomainError(result.Error);
                }

                await weeklyDutyPlanRepository.SaveAsync(
                    plan,
                    request.ExpectedVersion ?? loaded.Value.Version,
                    token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, plan, token);
                return ApplicationResult<DomainUnit>.Success(DomainUnit.Value);
            },
            ct);
    }
}

using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.UseCases.CleaningAreas;

public sealed record ApplyDueWeekRuleChangesRequest : ICommand<ApplicationResult<ApplyDueWeekRuleChangesResponse>>
{
    public WeekId? CurrentWeek { get; init; }
}

public sealed record ApplyDueWeekRuleChangesResponse(int AppliedCount);

public sealed class ApplyDueWeekRuleChangesUseCase(
    ICleaningAreaRepository cleaningAreaRepository,
    IApplicationTransaction transaction,
    IClock clock)
    : ICommandHandler<ApplyDueWeekRuleChangesRequest, ApplicationResult<ApplyDueWeekRuleChangesResponse>>
{
    public Task<ApplicationResult<ApplyDueWeekRuleChangesResponse>> Handle(ApplyDueWeekRuleChangesRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                var currentWeek = request.CurrentWeek ?? WeekId.FromDate(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));
                var dueAreas = await cleaningAreaRepository.ListWeekRuleDueAsync(currentWeek, token);

                var appliedCount = 0;
                foreach (var loaded in dueAreas)
                {
                    var aggregate = loaded.Aggregate;
                    var pending = aggregate.PendingWeekRule;
                    if (pending is null || pending.Value.EffectiveFromWeek.CompareTo(currentWeek) > 0)
                    {
                        continue;
                    }

                    var applyResult = aggregate.ApplyPendingWeekRule(currentWeek);
                    if (applyResult.IsFailure)
                    {
                        continue;
                    }

                    await cleaningAreaRepository.SaveAsync(aggregate, loaded.Version, token);
                    appliedCount++;
                }

                return ApplicationResult<ApplyDueWeekRuleChangesResponse>.Success(
                    new ApplyDueWeekRuleChangesResponse(appliedCount));
            },
            ct);
    }
}

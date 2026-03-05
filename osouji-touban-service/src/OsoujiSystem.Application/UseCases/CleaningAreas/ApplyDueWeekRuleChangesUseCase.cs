using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.Time;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.UseCases.CleaningAreas;

public sealed record ApplyDueWeekRuleChangesRequest : IRequest<ApplicationResult<ApplyDueWeekRuleChangesResponse>>
{
    public WeekId? CurrentWeek { get; init; }
}

public sealed record ApplyDueWeekRuleChangesResponse(int AppliedCount);

public sealed class ApplyDueWeekRuleChangesUseCase
    : IRequestHandler<ApplyDueWeekRuleChangesRequest, ApplicationResult<ApplyDueWeekRuleChangesResponse>>
{
    private readonly ICleaningAreaRepository _cleaningAreaRepository;
    private readonly IApplicationTransaction _transaction;
    private readonly IClock _clock;

    public ApplyDueWeekRuleChangesUseCase(
        ICleaningAreaRepository cleaningAreaRepository,
        IApplicationTransaction transaction,
        IClock clock)
    {
        _cleaningAreaRepository = cleaningAreaRepository;
        _transaction = transaction;
        _clock = clock;
    }

    public Task<ApplicationResult<ApplyDueWeekRuleChangesResponse>> Handle(ApplyDueWeekRuleChangesRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            _transaction,
            async token =>
            {
                var currentWeek = request.CurrentWeek ?? WeekId.FromDate(DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime));
                var dueAreas = await _cleaningAreaRepository.ListWeekRuleDueAsync(currentWeek, token);

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

                    await _cleaningAreaRepository.SaveAsync(aggregate, loaded.Version, token);
                    appliedCount++;
                }

                return ApplicationResult<ApplyDueWeekRuleChangesResponse>.Success(
                    new ApplyDueWeekRuleChangesResponse(appliedCount));
            },
            ct);
    }
}

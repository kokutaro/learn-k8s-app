using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.DomainServices;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Infrastructure.Persistence;

internal sealed class StubCleaningAreaRepository : ICleaningAreaRepository
{
    public Task<LoadedAggregate<CleaningArea>?> FindByIdAsync(CleaningAreaId areaId, CancellationToken ct)
        => Task.FromResult<LoadedAggregate<CleaningArea>?>(null);

    public Task<LoadedAggregate<CleaningArea>?> FindByUserIdAsync(UserId userId, CancellationToken ct)
        => Task.FromResult<LoadedAggregate<CleaningArea>?>(null);

    public Task<IReadOnlyList<LoadedAggregate<CleaningArea>>> ListAllAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<LoadedAggregate<CleaningArea>>>([]);

    public Task<IReadOnlyList<LoadedAggregate<CleaningArea>>> ListWeekRuleDueAsync(WeekId currentWeek, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<LoadedAggregate<CleaningArea>>>([]);

    public Task AddAsync(CleaningArea aggregate, CancellationToken ct) => Task.CompletedTask;

    public Task SaveAsync(CleaningArea aggregate, AggregateVersion expectedVersion, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class StubWeeklyDutyPlanRepository : IWeeklyDutyPlanRepository
{
    public Task<LoadedAggregate<WeeklyDutyPlan>?> FindByIdAsync(WeeklyDutyPlanId planId, CancellationToken ct)
        => Task.FromResult<LoadedAggregate<WeeklyDutyPlan>?>(null);

    public Task<LoadedAggregate<WeeklyDutyPlan>?> FindByAreaAndWeekAsync(CleaningAreaId areaId, WeekId weekId, CancellationToken ct)
        => Task.FromResult<LoadedAggregate<WeeklyDutyPlan>?>(null);

    public Task<IReadOnlyList<LoadedAggregate<WeeklyDutyPlan>>> ListAsync(
        CleaningAreaId? areaId,
        WeekId? weekId,
        WeeklyPlanStatus? status,
        CancellationToken ct)
        => Task.FromResult<IReadOnlyList<LoadedAggregate<WeeklyDutyPlan>>>([]);

    public Task AddAsync(WeeklyDutyPlan aggregate, CancellationToken ct) => Task.CompletedTask;

    public Task SaveAsync(WeeklyDutyPlan aggregate, AggregateVersion expectedVersion, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class StubAssignmentHistoryRepository : IAssignmentHistoryRepository
{
    public Task<IReadOnlyDictionary<UserId, AssignmentHistorySnapshot>> GetSnapshotsAsync(
        CleaningAreaId areaId,
        WeekId targetWeek,
        int windowWeeks,
        IReadOnlyCollection<UserId> userIds,
        CancellationToken ct)
    {
        var result = userIds.ToDictionary(
            userId => userId,
            userId => new AssignmentHistorySnapshot(userId, 0, 0));

        return Task.FromResult<IReadOnlyDictionary<UserId, AssignmentHistorySnapshot>>(result);
    }
}

internal sealed class StubApplicationTransaction : IApplicationTransaction
{
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => action(ct);
}

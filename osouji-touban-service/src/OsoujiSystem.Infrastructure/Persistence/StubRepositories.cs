using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.Queries.Abstractions;
using OsoujiSystem.Application.Queries.CleaningAreas;
using OsoujiSystem.Application.Queries.Facilities;
using OsoujiSystem.Application.Queries.Shared;
using OsoujiSystem.Application.Queries.WeeklyDutyPlans;
using OsoujiSystem.Domain.DomainServices;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.Facilities;
using OsoujiSystem.Domain.Entities.UserManagement;
using OsoujiSystem.Domain.Entities.UserManagement.ValueObjects;
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

internal sealed class StubFacilityRepository : IFacilityRepository
{
    public Task<LoadedAggregate<Facility>?> FindByIdAsync(FacilityId facilityId, CancellationToken ct)
        => Task.FromResult<LoadedAggregate<Facility>?>(null);

    public Task<LoadedAggregate<Facility>?> FindByCodeAsync(FacilityCode facilityCode, CancellationToken ct)
        => Task.FromResult<LoadedAggregate<Facility>?>(null);

    public Task AddAsync(Facility aggregate, CancellationToken ct) => Task.CompletedTask;

    public Task SaveAsync(Facility aggregate, AggregateVersion expectedVersion, CancellationToken ct) => Task.CompletedTask;
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

internal sealed class StubManagedUserRepository : IManagedUserRepository
{
    public Task<LoadedAggregate<ManagedUser>?> FindByIdAsync(UserId userId, CancellationToken ct)
        => Task.FromResult<LoadedAggregate<ManagedUser>?>(null);

    public Task<LoadedAggregate<ManagedUser>?> FindByEmployeeNumberAsync(EmployeeNumber employeeNumber, CancellationToken ct)
        => Task.FromResult<LoadedAggregate<ManagedUser>?>(null);

    public Task<LoadedAggregate<ManagedUser>?> FindByIdentityLinkAsync(
        IdentityProviderKey identityProviderKey,
        IdentitySubject identitySubject,
        CancellationToken ct)
        => Task.FromResult<LoadedAggregate<ManagedUser>?>(null);

    public Task AddAsync(ManagedUser aggregate, CancellationToken ct) => Task.CompletedTask;

    public Task SaveAsync(ManagedUser aggregate, AggregateVersion expectedVersion, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class StubUserDirectoryProjectionRepository : IUserDirectoryProjectionRepository
{
    public Task<UserDirectoryProjection?> FindByUserIdAsync(UserId userId, CancellationToken ct)
        => Task.FromResult<UserDirectoryProjection?>(null);

    public Task UpsertAsync(UserDirectoryProjection projection, long aggregateVersion, Guid sourceEventId, CancellationToken ct)
        => Task.CompletedTask;
}

internal sealed class StubFacilityDirectoryProjectionRepository : IFacilityDirectoryProjectionRepository
{
    public Task<FacilityDirectoryProjection?> FindByFacilityIdAsync(FacilityId facilityId, CancellationToken ct)
        => Task.FromResult<FacilityDirectoryProjection?>(null);

    public Task UpsertAsync(FacilityDirectoryProjection projection, long aggregateVersion, Guid sourceEventId, CancellationToken ct)
        => Task.CompletedTask;
}

internal sealed class StubApplicationTransaction : IApplicationTransaction
{
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => action(ct);
}

internal sealed class StubCleaningAreaReadRepository : ICleaningAreaReadRepository
{
    public Task<CursorPage<CleaningAreaListItemReadModel>> ListAsync(ListCleaningAreasQuery query, CancellationToken ct)
        => Task.FromResult(new CursorPage<CleaningAreaListItemReadModel>([], query.Limit, false, null));

    public Task<CleaningAreaDetailReadModel?> FindByIdAsync(Guid areaId, CancellationToken ct)
        => Task.FromResult<CleaningAreaDetailReadModel?>(null);
}

internal sealed class StubFacilityReadRepository : IFacilityReadRepository
{
    public Task<CursorPage<FacilityListItemReadModel>> ListAsync(ListFacilitiesQuery query, CancellationToken ct)
        => Task.FromResult(new CursorPage<FacilityListItemReadModel>([], query.Limit, false, null));

    public Task<FacilityDetailReadModel?> FindByIdAsync(Guid facilityId, CancellationToken ct)
        => Task.FromResult<FacilityDetailReadModel?>(null);
}

internal sealed class StubWeeklyDutyPlanReadRepository : IWeeklyDutyPlanReadRepository
{
    public Task<CursorPage<WeeklyDutyPlanListItemReadModel>> ListAsync(ListWeeklyDutyPlansQuery query, CancellationToken ct)
        => Task.FromResult(new CursorPage<WeeklyDutyPlanListItemReadModel>([], query.Limit, false, null));

    public Task<WeeklyDutyPlanDetailReadModel?> FindByIdAsync(Guid planId, CancellationToken ct)
        => Task.FromResult<WeeklyDutyPlanDetailReadModel?>(null);
}

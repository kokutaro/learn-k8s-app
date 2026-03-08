using OsoujiSystem.Domain.DomainServices;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.Facilities;
using OsoujiSystem.Domain.Entities.UserManagement;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Entities.UserManagement.ValueObjects;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Repositories;

public readonly record struct AggregateVersion(long Value)
{
    public static AggregateVersion Initial => new(1);
    public AggregateVersion Next() => new(Value + 1);
}

public readonly record struct LoadedAggregate<TAggregate>(
    TAggregate Aggregate,
    AggregateVersion Version);

public sealed class RepositoryConcurrencyException(string message) : Exception(message);

public sealed class RepositoryDuplicateException(string message) : Exception(message);

public interface ICleaningAreaRepository
{
    Task<LoadedAggregate<CleaningArea>?> FindByIdAsync(
        CleaningAreaId areaId,
        CancellationToken ct);

    Task<LoadedAggregate<CleaningArea>?> FindByUserIdAsync(
        UserId userId,
        CancellationToken ct);

    Task<IReadOnlyList<LoadedAggregate<CleaningArea>>> ListAllAsync(
        CancellationToken ct);

    Task<IReadOnlyList<LoadedAggregate<CleaningArea>>> ListWeekRuleDueAsync(
        WeekId currentWeek,
        CancellationToken ct);

    Task AddAsync(
        CleaningArea aggregate,
        CancellationToken ct);

    Task SaveAsync(
        CleaningArea aggregate,
        AggregateVersion expectedVersion,
        CancellationToken ct);
}

public interface IFacilityRepository
{
    Task<LoadedAggregate<Facility>?> FindByIdAsync(
        FacilityId facilityId,
        CancellationToken ct);

    Task<LoadedAggregate<Facility>?> FindByCodeAsync(
        FacilityCode facilityCode,
        CancellationToken ct);

    Task AddAsync(
        Facility aggregate,
        CancellationToken ct);

    Task SaveAsync(
        Facility aggregate,
        AggregateVersion expectedVersion,
        CancellationToken ct);
}

public interface IWeeklyDutyPlanRepository
{
    Task<LoadedAggregate<WeeklyDutyPlan>?> FindByIdAsync(
        WeeklyDutyPlanId planId,
        CancellationToken ct);

    Task<LoadedAggregate<WeeklyDutyPlan>?> FindByAreaAndWeekAsync(
        CleaningAreaId areaId,
        WeekId weekId,
        CancellationToken ct);

    Task<IReadOnlyList<LoadedAggregate<WeeklyDutyPlan>>> ListAsync(
        CleaningAreaId? areaId,
        WeekId? weekId,
        WeeklyPlanStatus? status,
        CancellationToken ct);

    Task AddAsync(
        WeeklyDutyPlan aggregate,
        CancellationToken ct);

    Task SaveAsync(
        WeeklyDutyPlan aggregate,
        AggregateVersion expectedVersion,
        CancellationToken ct);
}

public interface IAssignmentHistoryRepository
{
    Task<IReadOnlyDictionary<UserId, AssignmentHistorySnapshot>> GetSnapshotsAsync(
        CleaningAreaId areaId,
        WeekId targetWeek,
        int windowWeeks,
        IReadOnlyCollection<UserId> userIds,
        CancellationToken ct);
}

public interface IManagedUserRepository
{
    Task<LoadedAggregate<ManagedUser>?> FindByIdAsync(
        UserId userId,
        CancellationToken ct);

    Task<LoadedAggregate<ManagedUser>?> FindByEmployeeNumberAsync(
        EmployeeNumber employeeNumber,
        CancellationToken ct);

    Task<LoadedAggregate<ManagedUser>?> FindByIdentityLinkAsync(
        IdentityProviderKey identityProviderKey,
        IdentitySubject identitySubject,
        CancellationToken ct);

    Task AddAsync(
        ManagedUser aggregate,
        CancellationToken ct);

    Task SaveAsync(
        ManagedUser aggregate,
        AggregateVersion expectedVersion,
        CancellationToken ct);
}

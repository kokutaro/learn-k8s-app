using OsoujiSystem.Application.Queries.CleaningAreas;
using OsoujiSystem.Application.Queries.Facilities;
using OsoujiSystem.Application.Queries.Shared;
using OsoujiSystem.Application.Queries.Users;
using OsoujiSystem.Application.Queries.WeeklyDutyPlans;

namespace OsoujiSystem.Application.Queries.Abstractions;

public interface IFacilityReadRepository
{
    Task<CursorPage<FacilityListItemReadModel>> ListAsync(
        ListFacilitiesQuery query,
        CancellationToken ct);

    Task<FacilityDetailReadModel?> FindByIdAsync(
        Guid facilityId,
        CancellationToken ct);
}

public interface ICleaningAreaReadRepository
{
    Task<CursorPage<CleaningAreaListItemReadModel>> ListAsync(
        ListCleaningAreasQuery query,
        CancellationToken ct);

    Task<CleaningAreaDetailReadModel?> FindByIdAsync(
        Guid areaId,
        CancellationToken ct);
}

public interface IWeeklyDutyPlanReadRepository
{
    Task<CursorPage<WeeklyDutyPlanListItemReadModel>> ListAsync(
        ListWeeklyDutyPlansQuery query,
        CancellationToken ct);

    Task<WeeklyDutyPlanDetailReadModel?> FindByIdAsync(
        Guid planId,
        CancellationToken ct);
}

public interface IUserReadRepository
{
    Task<CursorPage<UserListItemReadModel>> ListAsync(
        ListUsersQuery query,
        CancellationToken ct);

    Task<UserDetailReadModel?> FindByIdAsync(
        Guid userId,
        CancellationToken ct);
}

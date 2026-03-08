using OsoujiSystem.Application.Queries.CleaningAreas;
using OsoujiSystem.Application.Queries.Shared;
using OsoujiSystem.Application.Queries.WeeklyDutyPlans;

namespace OsoujiSystem.Application.Queries.Abstractions;

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

using Cortex.Mediator.Queries;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.Queries.Abstractions;
using OsoujiSystem.Application.Queries.Shared;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.Queries.CleaningAreas;

public enum CleaningAreaSortOrder
{
    NameAsc = 0,
    NameDesc = 1
}

public sealed record ListCleaningAreasQuery(
    Guid? FacilityId,
    Guid? UserId,
    string? Cursor,
    int Limit,
    CleaningAreaSortOrder Sort) : IQuery<CursorPage<CleaningAreaListItemReadModel>>;

public sealed class ListCleaningAreasQueryHandler(
    ICleaningAreaReadRepository repository)
    : IQueryHandler<ListCleaningAreasQuery, CursorPage<CleaningAreaListItemReadModel>>
{
    public Task<CursorPage<CleaningAreaListItemReadModel>> Handle(ListCleaningAreasQuery query, CancellationToken cancellationToken)
        => repository.ListAsync(query, cancellationToken);
}

public sealed record GetCleaningAreaQuery(Guid AreaId) : IQuery<CleaningAreaDetailReadModel?>;

public sealed class GetCleaningAreaQueryHandler(
    ICleaningAreaReadRepository repository)
    : IQueryHandler<GetCleaningAreaQuery, CleaningAreaDetailReadModel?>
{
    public Task<CleaningAreaDetailReadModel?> Handle(GetCleaningAreaQuery query, CancellationToken cancellationToken)
        => repository.FindByIdAsync(query.AreaId, cancellationToken);
}

public sealed record GetCleaningAreaCurrentWeekQuery(Guid AreaId) : IQuery<CleaningAreaCurrentWeekReadModel?>;

public sealed class GetCleaningAreaCurrentWeekQueryHandler(
    ICleaningAreaReadRepository repository,
    IClock clock)
    : IQueryHandler<GetCleaningAreaCurrentWeekQuery, CleaningAreaCurrentWeekReadModel?>
{
    public async Task<CleaningAreaCurrentWeekReadModel?> Handle(
        GetCleaningAreaCurrentWeekQuery query,
        CancellationToken cancellationToken)
    {
        var area = await repository.FindByIdAsync(query.AreaId, cancellationToken);
        if (area is null)
        {
            return null;
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(area.CurrentWeekRule.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(clock.UtcNow, timeZone);
        var weekId = WeekId.FromDate(DateOnly.FromDateTime(localNow.Date));

        return new CleaningAreaCurrentWeekReadModel(
            area.Id,
            area.CurrentWeekRule.TimeZoneId,
            weekId.ToString(),
            area.CurrentWeekRule.StartDay);
    }
}

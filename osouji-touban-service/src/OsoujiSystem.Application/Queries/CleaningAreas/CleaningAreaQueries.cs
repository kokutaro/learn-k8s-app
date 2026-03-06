using Cortex.Mediator.Queries;
using OsoujiSystem.Application.Queries.Abstractions;
using OsoujiSystem.Application.Queries.Shared;

namespace OsoujiSystem.Application.Queries.CleaningAreas;

public enum CleaningAreaSortOrder
{
    NameAsc = 0,
    NameDesc = 1
}

public sealed record ListCleaningAreasQuery(
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

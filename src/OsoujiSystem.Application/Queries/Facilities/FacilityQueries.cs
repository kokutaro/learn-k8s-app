using Cortex.Mediator.Queries;
using OsoujiSystem.Application.Queries.Abstractions;
using OsoujiSystem.Application.Queries.Shared;
using OsoujiSystem.Domain.Entities.Facilities;

namespace OsoujiSystem.Application.Queries.Facilities;

public enum FacilitySortOrder
{
    NameAsc = 0,
    NameDesc = 1,
    FacilityCodeAsc = 2,
    FacilityCodeDesc = 3
}

public sealed record ListFacilitiesQuery(
    string? Query,
    FacilityLifecycleStatus? Status,
    string? Cursor,
    int Limit,
    FacilitySortOrder Sort) : IQuery<CursorPage<FacilityListItemReadModel>>;

public sealed class ListFacilitiesQueryHandler(
    IFacilityReadRepository repository)
    : IQueryHandler<ListFacilitiesQuery, CursorPage<FacilityListItemReadModel>>
{
    public Task<CursorPage<FacilityListItemReadModel>> Handle(ListFacilitiesQuery query, CancellationToken cancellationToken)
        => repository.ListAsync(query, cancellationToken);
}

public sealed record GetFacilityQuery(Guid FacilityId) : IQuery<FacilityDetailReadModel?>;

public sealed class GetFacilityQueryHandler(
    IFacilityReadRepository repository)
    : IQueryHandler<GetFacilityQuery, FacilityDetailReadModel?>
{
    public Task<FacilityDetailReadModel?> Handle(GetFacilityQuery query, CancellationToken cancellationToken)
        => repository.FindByIdAsync(query.FacilityId, cancellationToken);
}

using Cortex.Mediator.Queries;
using OsoujiSystem.Application.Queries.Abstractions;
using OsoujiSystem.Application.Queries.Shared;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.Queries.WeeklyDutyPlans;

public enum WeeklyDutyPlanSortOrder
{
    WeekIdAsc = 0,
    WeekIdDesc = 1,
    CreatedAtAsc = 2,
    CreatedAtDesc = 3
}

public sealed record ListWeeklyDutyPlansQuery(
    Guid? AreaId,
    WeekId? WeekId,
    WeeklyPlanStatus? Status,
    string? Cursor,
    int Limit,
    WeeklyDutyPlanSortOrder Sort) : IQuery<CursorPage<WeeklyDutyPlanListItemReadModel>>;

public sealed class ListWeeklyDutyPlansQueryHandler(
    IWeeklyDutyPlanReadRepository repository)
    : IQueryHandler<ListWeeklyDutyPlansQuery, CursorPage<WeeklyDutyPlanListItemReadModel>>
{
    public Task<CursorPage<WeeklyDutyPlanListItemReadModel>> Handle(ListWeeklyDutyPlansQuery query, CancellationToken cancellationToken)
        => repository.ListAsync(query, cancellationToken);
}

public sealed record GetWeeklyDutyPlanQuery(Guid PlanId) : IQuery<WeeklyDutyPlanDetailReadModel?>;

public sealed class GetWeeklyDutyPlanQueryHandler(
    IWeeklyDutyPlanReadRepository repository)
    : IQueryHandler<GetWeeklyDutyPlanQuery, WeeklyDutyPlanDetailReadModel?>
{
    public Task<WeeklyDutyPlanDetailReadModel?> Handle(GetWeeklyDutyPlanQuery query, CancellationToken cancellationToken)
        => repository.FindByIdAsync(query.PlanId, cancellationToken);
}

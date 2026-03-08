using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Events;

public sealed record WeeklyPlanGenerated(
    WeeklyDutyPlanId PlanId,
    CleaningAreaId AreaId,
    WeekId WeekId,
    PlanRevision Revision) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record WeeklyPlanRecalculated(
    WeeklyDutyPlanId PlanId,
    CleaningAreaId AreaId,
    WeekId WeekId,
    PlanRevision Revision) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record DutyAssigned(
    WeeklyDutyPlanId PlanId,
    CleaningSpotId SpotId,
    UserId UserId,
    WeekId WeekId,
    PlanRevision Revision) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record DutyReassigned(
    WeeklyDutyPlanId PlanId,
    CleaningSpotId SpotId,
    UserId UserId,
    WeekId WeekId,
    PlanRevision Revision) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record UserMarkedOffDuty(
    WeeklyDutyPlanId PlanId,
    UserId UserId,
    WeekId WeekId,
    PlanRevision Revision) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record WeeklyPlanPublished(
    WeeklyDutyPlanId PlanId,
    WeekId WeekId,
    PlanRevision Revision) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record WeeklyPlanClosed(
    WeeklyDutyPlanId PlanId,
    WeekId WeekId,
    PlanRevision Revision) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

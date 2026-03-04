using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Events;

public sealed record CleaningAreaRegistered(
    CleaningAreaId AreaId,
    string Name,
    WeekRule WeekRule) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record WeekRuleChangeScheduled(
    CleaningAreaId AreaId,
    WeekRule WeekRule) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CleaningSpotAdded(
    CleaningAreaId AreaId,
    CleaningSpotId SpotId,
    string SpotName) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CleaningSpotRemoved(
    CleaningAreaId AreaId,
    CleaningSpotId SpotId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record UserAssignedToArea(
    CleaningAreaId AreaId,
    UserId UserId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record UserUnassignedFromArea(
    CleaningAreaId AreaId,
    UserId UserId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record UserTransferredFromArea(
    CleaningAreaId AreaId,
    UserId UserId,
    CleaningAreaId? ToAreaId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

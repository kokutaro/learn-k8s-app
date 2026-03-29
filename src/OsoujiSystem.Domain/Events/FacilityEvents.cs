using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.Facilities;

namespace OsoujiSystem.Domain.Events;

public sealed record FacilityRegistered(
    Guid FacilityId,
    string FacilityCode,
    string Name,
    string? Description,
    string TimeZoneId,
    FacilityLifecycleStatus LifecycleStatus) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record FacilityUpdated(
    Guid FacilityId,
    string FacilityCode,
    string Name,
    string? Description,
    string TimeZoneId,
    FacilityLifecycleStatus LifecycleStatus,
    FacilityChangeType ChangeType,
    IReadOnlyList<string> ChangedFields) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

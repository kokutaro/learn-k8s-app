namespace OsoujiSystem.Application.Queries.Facilities;

public sealed record FacilityListItemReadModel(
    Guid Id,
    string FacilityCode,
    string Name,
    string TimeZoneId,
    string LifecycleStatus,
    long Version);

public sealed record FacilityDetailReadModel(
    Guid Id,
    string FacilityCode,
    string Name,
    string? Description,
    string TimeZoneId,
    string LifecycleStatus,
    long Version);

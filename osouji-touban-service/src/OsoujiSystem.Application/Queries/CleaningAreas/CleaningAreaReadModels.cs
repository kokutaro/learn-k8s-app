using OsoujiSystem.Application.Queries.Shared;

namespace OsoujiSystem.Application.Queries.CleaningAreas;

public sealed record CleaningAreaListItemReadModel(
    Guid Id,
    Guid FacilityId,
    string Name,
    WeekRuleReadModel CurrentWeekRule,
    long MemberCount,
    long SpotCount,
    long Version);

public sealed record CleaningSpotReadModel(
    Guid Id,
    string Name,
    int SortOrder);

public sealed record AreaMemberReadModel(
    Guid Id,
    Guid UserId,
    string EmployeeNumber);

public sealed record CleaningAreaDetailReadModel(
    Guid Id,
    Guid FacilityId,
    string Name,
    WeekRuleReadModel CurrentWeekRule,
    WeekRuleReadModel? PendingWeekRule,
    int RotationCursor,
    IReadOnlyList<CleaningSpotReadModel> Spots,
    IReadOnlyList<AreaMemberReadModel> Members,
    long Version);

public sealed record CleaningAreaCurrentWeekReadModel(
    Guid AreaId,
    string TimeZoneId,
    string WeekId,
    string WeekStartDay);

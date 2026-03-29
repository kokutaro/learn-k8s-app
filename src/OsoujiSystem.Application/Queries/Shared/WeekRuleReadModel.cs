namespace OsoujiSystem.Application.Queries.Shared;

public sealed record WeekRuleReadModel(
    string StartDay,
    string StartTime,
    string TimeZoneId,
    string EffectiveFromWeek);

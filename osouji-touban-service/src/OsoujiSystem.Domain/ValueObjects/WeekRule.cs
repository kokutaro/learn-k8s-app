using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Errors;

namespace OsoujiSystem.Domain.ValueObjects;

public readonly record struct WeekRule(
    DayOfWeek StartDay,
    TimeOnly StartTime,
    string TimeZoneId,
    WeekId EffectiveFromWeek)
{
    public static Result<WeekRule, DomainError> Create(
        DayOfWeek startDay,
        TimeOnly startTime,
        string timeZoneId,
        WeekId effectiveFromWeek)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return Result<WeekRule, DomainError>.Failure(new InvalidWeekRuleTimeZoneError(timeZoneId));
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return Result<WeekRule, DomainError>.Failure(new InvalidWeekRuleTimeZoneError(timeZoneId));
        }
        catch (InvalidTimeZoneException)
        {
            return Result<WeekRule, DomainError>.Failure(new InvalidWeekRuleTimeZoneError(timeZoneId));
        }

        return Result<WeekRule, DomainError>.Success(
            new WeekRule(startDay, startTime, timeZoneId, effectiveFromWeek));
    }

    public override string ToString() => $"{StartDay} {StartTime} ({TimeZoneId}) from {EffectiveFromWeek}";
}

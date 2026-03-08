using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.Time;

internal static class WeekContextResolver
{
    public static WeekId ResolveCurrentWeek(IClock clock, WeekRule weekRule)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(weekRule.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(clock.UtcNow, tz);
        return WeekId.FromDate(DateOnly.FromDateTime(localNow.Date));
    }
}

using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.WebApi.Endpoints.Support;

internal static class WeekDisplayFormatter
{
    public static string ToWeekLabel(WeekId weekId, DayOfWeek firstDayOfWeek)
        => $"{weekId.GetStartDate(firstDayOfWeek):yyyy/M/d} 週";

    public static string ToWeekLabel(string weekId, string firstDayOfWeek)
    {
        if (!ApiRequestParsing.TryParseWeekId(weekId, out var parsedWeekId, out _)
            || !Enum.TryParse<DayOfWeek>(firstDayOfWeek, ignoreCase: true, out var parsedDayOfWeek))
        {
            return weekId;
        }

        return ToWeekLabel(parsedWeekId, parsedDayOfWeek);
    }

    public static DayOfWeek ResolveWeekStartDay(WeekId targetWeek, WeekRule currentWeekRule, WeekRule? pendingWeekRule)
    {
        if (pendingWeekRule.HasValue && pendingWeekRule.Value.EffectiveFromWeek.CompareTo(targetWeek) <= 0)
        {
            return pendingWeekRule.Value.StartDay;
        }

        return currentWeekRule.StartDay;
    }
}

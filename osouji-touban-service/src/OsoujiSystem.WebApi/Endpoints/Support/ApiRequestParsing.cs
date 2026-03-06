using System.Text;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.WebApi.Endpoints.Support;

internal static class ApiRequestParsing
{
    public static bool TryParseGuidId<TId>(string? raw, Func<Guid, TId> factory, out TId value)
        where TId : struct
    {
        value = default;
        return Guid.TryParse(raw, out var guid) && Assign(factory(guid), out value);
    }

    public static bool TryParseWeekId(string? raw, out WeekId weekId, out DomainError? error)
    {
        weekId = default;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = new InvalidWeekIdError(0, 0);
            return false;
        }

        var trimmed = raw.Trim().ToUpperInvariant();
        var parts = trimmed.Split("-W", StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var year)
            || !int.TryParse(parts[1], out var weekNumber))
        {
            error = new InvalidWeekIdError(0, 0);
            return false;
        }

        var result = WeekId.Create(year, weekNumber);
        if (result.IsFailure)
        {
            error = result.Error;
            return false;
        }

        weekId = result.Value;
        return true;
    }

    public static bool TryParseWeekRule(
        string? startDay,
        string? startTime,
        string? timeZoneId,
        string? effectiveFromWeek,
        out WeekRule weekRule,
        out IDictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>();
        weekRule = default;

        if (!TryParseDayOfWeek(startDay, out var dayOfWeek))
        {
            errors["startDay"] = ["Expected one of: sunday, monday, tuesday, wednesday, thursday, friday, saturday."];
        }

        if (!TimeOnly.TryParse(startTime, out var time))
        {
            errors["startTime"] = ["Expected HH:mm:ss or a valid TimeOnly value."];
        }

        if (!TryParseWeekId(effectiveFromWeek, out var weekId, out var weekError))
        {
            errors["effectiveFromWeek"] = [weekError?.Message ?? "Invalid week id."];
        }

        if (errors.Count > 0)
        {
            return false;
        }

        var result = WeekRule.Create(dayOfWeek, time, timeZoneId ?? string.Empty, weekId);
        if (result.IsFailure)
        {
            errors["timeZoneId"] = [result.Error.Message];
            return false;
        }

        weekRule = result.Value;
        return true;
    }

    public static bool TryParseEmployeeNumber(string? raw, out EmployeeNumber employeeNumber, out string message)
    {
        employeeNumber = default;
        message = string.Empty;

        var result = EmployeeNumber.Create(raw ?? string.Empty);
        if (result.IsFailure)
        {
            message = result.Error.Message;
            return false;
        }

        employeeNumber = result.Value;
        return true;
    }

    public static bool TryParseWeeklyPlanStatus(string? raw, out WeeklyPlanStatus status)
    {
        status = default;
        return raw?.Trim().ToLowerInvariant() switch
        {
            "draft" => Assign(WeeklyPlanStatus.Draft, out status),
            "published" => Assign(WeeklyPlanStatus.Published, out status),
            "closed" => Assign(WeeklyPlanStatus.Closed, out status),
            _ => false
        };
    }

    public static string EncodeCursor(int offset)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString()));

    public static int DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return 0;
        }

        try
        {
            var bytes = Convert.FromBase64String(cursor);
            var text = Encoding.UTF8.GetString(bytes);
            return int.TryParse(text, out var offset) && offset >= 0 ? offset : 0;
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    public static string ToApiStatus(WeeklyPlanStatus status) => status switch
    {
        WeeklyPlanStatus.Draft => "draft",
        WeeklyPlanStatus.Published => "published",
        WeeklyPlanStatus.Closed => "closed",
        _ => status.ToString().ToLowerInvariant()
    };

    public static string ToApiDayOfWeek(DayOfWeek value) => value.ToString().ToLowerInvariant();

    private static bool TryParseDayOfWeek(string? raw, out DayOfWeek value)
    {
        return Enum.TryParse(raw, ignoreCase: true, out value);
    }

    private static bool Assign<T>(T source, out T destination)
    {
        destination = source;
        return true;
    }
}

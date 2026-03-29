using System.Text;
using System.Text.Json;
using OsoujiSystem.Application.Queries.Shared;
using OsoujiSystem.Domain.ValueObjects;
using OsoujiSystem.Infrastructure.Serialization;

namespace OsoujiSystem.Infrastructure.Queries.Postgres;

internal sealed class PostgresReadModelHelpers(InfrastructureJsonSerializer jsonSerializer)
{
    public WeekRuleReadModel DeserializeWeekRule(string json)
    {
        var rule = jsonSerializer.Deserialize<WeekRule?>(json);
        if (!rule.HasValue)
        {
            throw new InvalidOperationException("Failed to deserialize week rule projection.");
        }

        return new WeekRuleReadModel(
            rule.Value.StartDay.ToString().ToLowerInvariant(),
            rule.Value.StartTime.ToString("HH:mm:ss"),
            rule.Value.TimeZoneId,
            rule.Value.EffectiveFromWeek.ToString());
    }

    public WeekRuleReadModel? DeserializeWeekRuleOrNull(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : DeserializeWeekRule(json);

    public string ResolveWeekStartDay(string currentWeekRuleJson, string? pendingWeekRuleJson, int year, int weekNumber)
    {
        var currentWeekRule = DeserializeWeekRule(currentWeekRuleJson);
        var pendingWeekRule = DeserializeWeekRuleOrNull(pendingWeekRuleJson);
        var targetWeek = new WeekId(year, weekNumber);

        if (pendingWeekRule is not null
            && TryParseWeekId(pendingWeekRule.EffectiveFromWeek, out var effectiveFromWeek)
            && effectiveFromWeek.CompareTo(targetWeek) <= 0)
        {
            return pendingWeekRule.StartDay;
        }

        return currentWeekRule.StartDay;
    }

    public string EncodeCursor<TCursor>(TCursor cursor)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonSerializer.Serialize(cursor)));

    public bool TryDecodeCursor<TCursor>(string? raw, out TCursor? cursor)
        where TCursor : class
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            cursor = jsonSerializer.Deserialize<TCursor>(json);
            return cursor is not null;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string ToWeeklyPlanStatus(short status) => status switch
    {
        0 => "draft",
        1 => "published",
        2 => "closed",
        _ => status.ToString()
    };

    public static string ToWeekId(int year, int weekNumber) => new WeekId(year, weekNumber).ToString();

    private static bool TryParseWeekId(string raw, out WeekId weekId)
    {
        weekId = default;

        var parts = raw.Split("-W", StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var year)
            || !int.TryParse(parts[1], out var weekNumber))
        {
            return false;
        }

        var result = WeekId.Create(year, weekNumber);
        if (result.IsFailure)
        {
            return false;
        }

        weekId = result.Value;
        return true;
    }
}

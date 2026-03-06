using System.Text;
using System.Text.Json;
using OsoujiSystem.Application.Queries.Shared;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Infrastructure.Queries.Postgres;

internal static class PostgresReadModelHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static WeekRuleReadModel DeserializeWeekRule(string json)
    {
        var rule = JsonSerializer.Deserialize<WeekRule?>(json, JsonOptions);
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

    public static WeekRuleReadModel? DeserializeWeekRuleOrNull(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : DeserializeWeekRule(json);

    public static string EncodeCursor<TCursor>(TCursor cursor)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cursor, JsonOptions)));

    public static bool TryDecodeCursor<TCursor>(string? raw, out TCursor? cursor)
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
            cursor = JsonSerializer.Deserialize<TCursor>(json, JsonOptions);
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
}

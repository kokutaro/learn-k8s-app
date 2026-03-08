using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace OsoujiSystem.Infrastructure.Messaging;

internal static class RabbitMqTraceContext
{
    internal const string TraceIdHeader = "trace_id";
    internal const string TraceParentHeader = "traceparent";
    internal const string TraceStateHeader = "tracestate";
    internal const string CorrelationIdHeader = "correlation_id";

    internal static void Inject(Activity? activity, IDictionary<string, object?> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (activity is null)
        {
            return;
        }

        headers[TraceIdHeader] = activity.TraceId.ToString();
        if (!headers.ContainsKey(CorrelationIdHeader) || headers[CorrelationIdHeader] is null)
        {
            headers[CorrelationIdHeader] = activity.TraceId.ToString();
        }

        if (!string.IsNullOrWhiteSpace(activity.Id))
        {
            headers[TraceParentHeader] = activity.Id;
        }

        if (string.IsNullOrWhiteSpace(activity.TraceStateString))
        {
            headers.Remove(TraceStateHeader);
            return;
        }

        headers[TraceStateHeader] = activity.TraceStateString;
    }

    internal static bool TryExtractParentContext(
        IReadOnlyDictionary<string, object?> headers,
        out ActivityContext parentContext)
    {
        ArgumentNullException.ThrowIfNull(headers);

        parentContext = default;

        if (!TryReadHeaderString(headers, TraceParentHeader, out var traceParent)
            || string.IsNullOrWhiteSpace(traceParent))
        {
            return false;
        }

        _ = TryReadHeaderString(headers, TraceStateHeader, out var traceState);
        return ActivityContext.TryParse(traceParent, traceState, isRemote: true, out parentContext);
    }

    internal static Dictionary<string, object?> DeserializePersistedHeaders(string serializedHeaders)
    {
        var rawHeaders = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(serializedHeaders) ?? [];
        var headers = new Dictionary<string, object?>(rawHeaders.Count, StringComparer.Ordinal);

        foreach (var pair in rawHeaders)
        {
            headers[pair.Key] = ConvertJsonElement(pair.Value);
        }

        return headers;
    }

    internal static Dictionary<string, object?> ToRabbitMqHeaders(IReadOnlyDictionary<string, object?> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        return headers
            .Where(pair => pair.Value is not null)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value switch
                {
                    JsonElement jsonElement => ConvertJsonElement(jsonElement),
                    Guid guid => guid.ToString("D"),
                    DateTimeOffset timestamp => timestamp.ToString("O"),
                    DateTime timestamp => timestamp.ToUniversalTime().ToString("O"),
                    _ => pair.Value
                },
                StringComparer.Ordinal);
    }

    private static bool TryReadHeaderString(
        IReadOnlyDictionary<string, object?> headers,
        string key,
        out string? value)
    {
        if (!headers.TryGetValue(key, out var rawValue) || rawValue is null)
        {
            value = null;
            return false;
        }

        switch (rawValue)
        {
            case string text:
                value = text;
                return true;
            case byte[] bytes:
                value = Encoding.UTF8.GetString(bytes);
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } element:
                value = element.GetString();
                return !string.IsNullOrWhiteSpace(value);
            default:
                value = rawValue.ToString();
                return !string.IsNullOrWhiteSpace(value);
        }
    }

    private static object? ConvertJsonElement(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt32(out var asInt32) => asInt32,
            JsonValueKind.Number when value.TryGetInt64(out var asInt64) => asInt64,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => value.GetRawText()
        };
}

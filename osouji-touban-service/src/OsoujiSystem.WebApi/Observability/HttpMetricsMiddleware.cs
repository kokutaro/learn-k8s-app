using System.Diagnostics;
using OsoujiSystem.Infrastructure.Observability;

namespace OsoujiSystem.WebApi.Observability;

internal sealed class HttpMetricsMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var start = Stopwatch.GetTimestamp();
        await next(context);
        var durationSeconds = Stopwatch.GetElapsedTime(start).TotalSeconds;

        var apiKind = ResolveApiKind(context.Request.Method);
        var statusClass = ResolveStatusClass(context.Response.StatusCode);

        OsoujiTelemetry.HttpRequestDurationSeconds.Record(
            durationSeconds,
            new KeyValuePair<string, object?>("api_kind", apiKind));

        OsoujiTelemetry.HttpRequestsTotal.Add(
            1,
            new KeyValuePair<string, object?>("status_class", statusClass),
            new KeyValuePair<string, object?>("api_kind", apiKind));
    }

    private static string ResolveApiKind(string method)
        => HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
            ? "query"
            : "command";

    private static string ResolveStatusClass(int statusCode)
    {
        var klass = statusCode / 100;
        return klass switch
        {
            2 => "2xx",
            3 => "3xx",
            4 => "4xx",
            5 => "5xx",
            _ => "other"
        };
    }
}

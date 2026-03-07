using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OsoujiSystem.Application.Queries.Abstractions;
using OsoujiSystem.Application.Queries.Shared;
using OsoujiSystem.Application.Queries.WeeklyDutyPlans;
using OsoujiSystem.Infrastructure.Observability;
using OsoujiSystem.Infrastructure.Options;

namespace OsoujiSystem.Infrastructure.Queries.Caching;

internal sealed class CachedWeeklyDutyPlanReadRepository(
    Postgres.PostgresWeeklyDutyPlanReadRepository origin,
    IReadModelCache cache,
    IReadModelCacheKeyFactory cacheKeys,
    IOptions<InfrastructureOptions> options) : IWeeklyDutyPlanReadRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeSpan _detailTtl = TimeSpan.FromSeconds(options.Value.Redis.ReadModelDetailTtlSeconds);
    private readonly TimeSpan _listTtl = TimeSpan.FromSeconds(options.Value.Redis.ReadModelListTtlSeconds);
    private readonly int _maxListLimit = options.Value.Redis.ReadModelCacheMaxListLimit;

    public async Task<CursorPage<WeeklyDutyPlanListItemReadModel>> ListAsync(ListWeeklyDutyPlansQuery query, CancellationToken ct)
    {
        if (query.Limit > _maxListLimit)
        {
            TrackRequest("list", "bypass");
            return await origin.ListAsync(query, ct);
        }

        try
        {
            var namespaceVersion = await cache.GetNamespaceVersionAsync(cacheKeys.WeeklyDutyPlansListNamespace(), ct);
            var key = cacheKeys.WeeklyDutyPlansListResult(namespaceVersion, HashQuerySignature(BuildQuerySignature(query)));
            var cached = await cache.TryGetAsync<CursorPage<WeeklyDutyPlanListItemReadModel>>(key, ct);
            if (cached is not null)
            {
                TrackRequest("list", "hit");
                return cached;
            }

            TrackRequest("list", "miss");
            return await FillListAsync(query, key, ct);
        }
        catch
        {
            TrackRequest("list", "error");
            return await origin.ListAsync(query, ct);
        }
    }

    public async Task<WeeklyDutyPlanDetailReadModel?> FindByIdAsync(Guid planId, CancellationToken ct)
    {
        var latestKey = cacheKeys.WeeklyDutyPlanDetailLatest(planId);

        try
        {
            var latest = await cache.TryGetStringAsync(latestKey, ct);
            if (long.TryParse(latest, out var version))
            {
                var cached = await cache.TryGetAsync<WeeklyDutyPlanDetailReadModel>(
                    cacheKeys.WeeklyDutyPlanDetailVersion(planId, version),
                    ct);
                if (cached is not null)
                {
                    TrackRequest("detail", "hit");
                    return cached;
                }
            }

            TrackRequest("detail", "miss");
            return await FillDetailAsync(planId, latestKey, ct);
        }
        catch
        {
            TrackRequest("detail", "error");
            return await origin.FindByIdAsync(planId, ct);
        }
    }

    private async Task<CursorPage<WeeklyDutyPlanListItemReadModel>> FillListAsync(
        ListWeeklyDutyPlansQuery query,
        string cacheKey,
        CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var page = await origin.ListAsync(query, ct);
        await cache.SetAsync(cacheKey, page, _listTtl, ct);
        TrackPayload("list", page);
        TrackFill("list", start);
        return page;
    }

    private async Task<WeeklyDutyPlanDetailReadModel?> FillDetailAsync(
        Guid planId,
        string latestKey,
        CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var value = await origin.FindByIdAsync(planId, ct);
        if (value is null)
        {
            TrackFill("detail", start);
            return null;
        }

        var versionKey = cacheKeys.WeeklyDutyPlanDetailVersion(planId, value.Version);
        await cache.SetAsync(versionKey, value, _detailTtl, ct);
        await cache.SetStringAsync(latestKey, value.Version.ToString(), _detailTtl, ct);
        TrackPayload("detail", value);
        TrackFill("detail", start);
        return value;
    }

    private static string BuildQuerySignature(ListWeeklyDutyPlansQuery query)
        => $"areaId={query.AreaId?.ToString("D") ?? string.Empty}|weekId={query.WeekId?.ToString() ?? string.Empty}|status={(int?)query.Status}|cursor={query.Cursor ?? string.Empty}|limit={query.Limit}|sort={(int)query.Sort}";

    private static string HashQuerySignature(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void TrackRequest(string operation, string result)
        => OsoujiTelemetry.ReadModelCacheRequestsTotal.Add(
            1,
            new KeyValuePair<string, object?>("resource", "weekly_plan"),
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("result", result));

    private static void TrackFill(string operation, long startTimestamp)
    {
        OsoujiTelemetry.ReadModelCacheRequestsTotal.Add(
            1,
            new KeyValuePair<string, object?>("resource", "weekly_plan"),
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("result", "fill"));
        OsoujiTelemetry.ReadModelCacheFillDurationSeconds.Record(
            Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
            new KeyValuePair<string, object?>("resource", "weekly_plan"),
            new KeyValuePair<string, object?>("operation", operation));
    }

    private static void TrackPayload<T>(string operation, T payload)
        => OsoujiTelemetry.ReadModelCachePayloadBytes.Record(
            JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions).Length,
            new KeyValuePair<string, object?>("resource", "weekly_plan"),
            new KeyValuePair<string, object?>("operation", operation));
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OsoujiSystem.Application.Queries.Abstractions;
using OsoujiSystem.Application.Queries.Facilities;
using OsoujiSystem.Application.Queries.Shared;
using OsoujiSystem.Infrastructure.Observability;
using OsoujiSystem.Infrastructure.Options;
using OsoujiSystem.Infrastructure.Serialization;

namespace OsoujiSystem.Infrastructure.Queries.Caching;

internal sealed class CachedFacilityReadRepository(
    Postgres.PostgresFacilityReadRepository origin,
    IReadModelCache cache,
    IReadModelCacheKeyFactory cacheKeys,
    IOptions<InfrastructureOptions> options,
    InfrastructureJsonSerializer jsonSerializer) : IFacilityReadRepository
{
    private readonly TimeSpan _detailTtl = TimeSpan.FromSeconds(options.Value.Redis.ReadModelDetailTtlSeconds);
    private readonly TimeSpan _listTtl = TimeSpan.FromSeconds(options.Value.Redis.ReadModelListTtlSeconds);
    private readonly int _maxListLimit = options.Value.Redis.ReadModelCacheMaxListLimit;

    public async Task<CursorPage<FacilityListItemReadModel>> ListAsync(ListFacilitiesQuery query, CancellationToken ct)
    {
        if (query.Limit > _maxListLimit)
        {
            TrackRequest("list", "bypass");
            return await origin.ListAsync(query, ct);
        }

        try
        {
            var namespaceVersion = await cache.GetNamespaceVersionAsync(cacheKeys.FacilitiesListNamespace(), ct);
            var key = cacheKeys.FacilitiesListResult(namespaceVersion, HashQuerySignature(BuildQuerySignature(query)));
            var cached = await cache.TryGetAsync<CursorPage<FacilityListItemReadModel>>(key, ct);
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

    public async Task<FacilityDetailReadModel?> FindByIdAsync(Guid facilityId, CancellationToken ct)
    {
        var latestKey = cacheKeys.FacilityDetailLatest(facilityId);

        try
        {
            var latest = await cache.TryGetStringAsync(latestKey, ct);
            if (long.TryParse(latest, out var version))
            {
                var versioned = await cache.TryGetAsync<FacilityDetailReadModel>(
                    cacheKeys.FacilityDetailVersion(facilityId, version),
                    ct);
                if (versioned is not null)
                {
                    TrackRequest("detail", "hit");
                    return versioned;
                }
            }

            TrackRequest("detail", "miss");
            return await FillDetailAsync(facilityId, latestKey, ct);
        }
        catch
        {
            TrackRequest("detail", "error");
            return await origin.FindByIdAsync(facilityId, ct);
        }
    }

    private async Task<CursorPage<FacilityListItemReadModel>> FillListAsync(
        ListFacilitiesQuery query,
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

    private async Task<FacilityDetailReadModel?> FillDetailAsync(
        Guid facilityId,
        string latestKey,
        CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var value = await origin.FindByIdAsync(facilityId, ct);
        if (value is null)
        {
            TrackFill("detail", start);
            return null;
        }

        var versionKey = cacheKeys.FacilityDetailVersion(facilityId, value.Version);
        await cache.SetAsync(versionKey, value, _detailTtl, ct);
        await cache.SetStringAsync(latestKey, value.Version.ToString(), _detailTtl, ct);
        TrackPayload("detail", value);
        TrackFill("detail", start);
        return value;
    }

    private static string BuildQuerySignature(ListFacilitiesQuery query)
        => $"query={query.Query ?? string.Empty}|status={query.Status?.ToString() ?? string.Empty}|cursor={query.Cursor ?? string.Empty}|limit={query.Limit}|sort={(int)query.Sort}";

    private static string HashQuerySignature(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void TrackRequest(string operation, string result)
        => OsoujiTelemetry.ReadModelCacheRequestsTotal.Add(
            1,
            new KeyValuePair<string, object?>("resource", "facility"),
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("result", result));

    private static void TrackFill(string operation, long startTimestamp)
    {
        OsoujiTelemetry.ReadModelCacheRequestsTotal.Add(
            1,
            new KeyValuePair<string, object?>("resource", "facility"),
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("result", "fill"));
        OsoujiTelemetry.ReadModelCacheFillDurationSeconds.Record(
            Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
            new KeyValuePair<string, object?>("resource", "facility"),
            new KeyValuePair<string, object?>("operation", operation));
    }

    private void TrackPayload<T>(string operation, T payload)
        => OsoujiTelemetry.ReadModelCachePayloadBytes.Record(
            jsonSerializer.SerializeToUtf8Bytes(payload).Length,
            new KeyValuePair<string, object?>("resource", "facility"),
            new KeyValuePair<string, object?>("operation", operation));
}

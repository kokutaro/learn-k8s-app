using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace OsoujiSystem.Infrastructure.Cache;

internal sealed class RedisAggregateCache : IAggregateCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDistributedCache _distributedCache;

    public RedisAggregateCache(IDistributedCache distributedCache)
    {
        _distributedCache = distributedCache;
    }

    public async Task<(long Version, string Payload)?> TryGetAsync(string key, CancellationToken ct)
    {
        var raw = await _distributedCache.GetStringAsync(key, ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var envelope = JsonSerializer.Deserialize<CacheEnvelope>(raw, JsonOptions);
        if (envelope is null)
        {
            return null;
        }

        return (envelope.Version, envelope.Payload);
    }

    public async Task SetAsync(string key, long version, string payload, TimeSpan ttl, CancellationToken ct)
    {
        var envelope = new CacheEnvelope(version, payload, DateTimeOffset.UtcNow);
        var serialized = JsonSerializer.Serialize(envelope, JsonOptions);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        };

        await _distributedCache.SetStringAsync(key, serialized, options, ct);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
        => _distributedCache.RemoveAsync(key, ct);
}

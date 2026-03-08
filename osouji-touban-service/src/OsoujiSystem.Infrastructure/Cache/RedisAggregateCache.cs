using Microsoft.Extensions.Caching.Distributed;
using OsoujiSystem.Infrastructure.Serialization;

namespace OsoujiSystem.Infrastructure.Cache;

internal sealed class RedisAggregateCache(
    IDistributedCache distributedCache,
    InfrastructureJsonSerializer jsonSerializer) : IAggregateCache
{
    public async Task<(long Version, string Payload)?> TryGetAsync(string key, CancellationToken ct)
    {
        var raw = await distributedCache.GetStringAsync(key, ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var envelope = jsonSerializer.Deserialize<CacheEnvelope>(raw);
        if (envelope is null)
        {
            return null;
        }

        return (envelope.Version, envelope.Payload);
    }

    public async Task SetAsync(string key, long version, string payload, TimeSpan ttl, CancellationToken ct)
    {
        var envelope = new CacheEnvelope(version, payload, DateTimeOffset.UtcNow);
        var serialized = jsonSerializer.Serialize(envelope);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        };

        await distributedCache.SetStringAsync(key, serialized, options, ct);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
        => distributedCache.RemoveAsync(key, ct);
}

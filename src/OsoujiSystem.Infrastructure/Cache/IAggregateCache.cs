namespace OsoujiSystem.Infrastructure.Cache;

internal interface IAggregateCache
{
    Task<(long Version, string Payload)?> TryGetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, long version, string payload, TimeSpan ttl, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

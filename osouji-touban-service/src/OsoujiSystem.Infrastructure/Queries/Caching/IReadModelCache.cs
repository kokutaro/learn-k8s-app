namespace OsoujiSystem.Infrastructure.Queries.Caching;

internal interface IReadModelCache
{
    Task<T?> TryGetAsync<T>(string key, CancellationToken ct);
    Task<string?> TryGetStringAsync(string key, CancellationToken ct);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct);
    Task SetStringAsync(string key, string value, TimeSpan ttl, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
    Task<long> GetNamespaceVersionAsync(string key, CancellationToken ct);
    Task<long> IncrementNamespaceVersionAsync(string key, TimeSpan ttl, CancellationToken ct);
}

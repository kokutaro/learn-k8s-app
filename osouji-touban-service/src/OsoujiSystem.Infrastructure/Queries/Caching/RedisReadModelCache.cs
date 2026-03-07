using System.Text.Json;
using StackExchange.Redis;

namespace OsoujiSystem.Infrastructure.Queries.Caching;

internal sealed class RedisReadModelCache(IConnectionMultiplexer connectionMultiplexer) : IReadModelCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();

    public async Task<T?> TryGetAsync<T>(string key, CancellationToken ct)
    {
        var value = await _database.StringGetAsync(key).WaitAsync(ct);
        if (!value.HasValue)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);
    }

    public async Task<string?> TryGetStringAsync(string key, CancellationToken ct)
    {
        var value = await _database.StringGetAsync(key).WaitAsync(ct);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(value, JsonOptions);
        await _database.StringSetAsync(key, payload, ttl).WaitAsync(ct);
    }

    public Task SetStringAsync(string key, string value, TimeSpan ttl, CancellationToken ct)
        => _database.StringSetAsync(key, value, ttl).WaitAsync(ct);

    public Task RemoveAsync(string key, CancellationToken ct)
        => _database.KeyDeleteAsync(key).WaitAsync(ct);

    public async Task<long> GetNamespaceVersionAsync(string key, CancellationToken ct)
    {
        var raw = await TryGetStringAsync(key, ct);
        return long.TryParse(raw, out var parsed) ? parsed : 0L;
    }

    public async Task<long> IncrementNamespaceVersionAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        var version = await _database.StringIncrementAsync(key).WaitAsync(ct);
        await _database.KeyExpireAsync(key, ttl).WaitAsync(ct);
        return version;
    }
}

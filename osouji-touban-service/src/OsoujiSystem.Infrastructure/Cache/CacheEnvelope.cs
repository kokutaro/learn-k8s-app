namespace OsoujiSystem.Infrastructure.Cache;

internal sealed record CacheEnvelope(long Version, string Payload, DateTimeOffset CachedAt);

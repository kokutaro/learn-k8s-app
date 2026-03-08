namespace OsoujiSystem.Infrastructure.Cache;

internal sealed record CacheInvalidationTask(Guid TaskId, string CacheKey, long ReasonGlobalPosition, int RetryCount);

internal interface ICacheInvalidationTaskRepository
{
    Task EnqueueAsync(string cacheKey, long reasonGlobalPosition, string? lastError, CancellationToken ct);
    Task<IReadOnlyList<CacheInvalidationTask>> ListDueAsync(int batchSize, DateTimeOffset utcNow, CancellationToken ct);
    Task MarkResolvedAsync(Guid taskId, CancellationToken ct);
    Task MarkFailedAsync(Guid taskId, int nextRetryCount, DateTimeOffset nextRetryAt, string lastError, CancellationToken ct);
}

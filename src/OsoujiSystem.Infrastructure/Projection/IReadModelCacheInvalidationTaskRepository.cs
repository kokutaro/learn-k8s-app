namespace OsoujiSystem.Infrastructure.Projection;

internal enum ReadModelCacheInvalidationOperationKind
{
    Remove = 0,
    IncrementNamespace = 1
}

internal sealed record ReadModelCacheInvalidationTask(
    Guid TaskId,
    string ProjectorName,
    string CacheKey,
    ReadModelCacheInvalidationOperationKind OperationKind,
    long ReasonGlobalPosition,
    int RetryCount);

internal interface IReadModelCacheInvalidationTaskRepository
{
    Task EnqueueAsync(
        string projectorName,
        string cacheKey,
        ReadModelCacheInvalidationOperationKind operationKind,
        long reasonGlobalPosition,
        string? lastError,
        CancellationToken ct);

    Task<IReadOnlyList<ReadModelCacheInvalidationTask>> ListDueAsync(
        int batchSize,
        DateTimeOffset utcNow,
        CancellationToken ct);

    Task MarkResolvedAsync(Guid taskId, CancellationToken ct);
    Task MarkFailedAsync(Guid taskId, int nextRetryCount, DateTimeOffset nextRetryAt, string lastError, CancellationToken ct);
}

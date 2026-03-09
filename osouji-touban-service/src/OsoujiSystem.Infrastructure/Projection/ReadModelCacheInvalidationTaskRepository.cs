using Dapper;
using Npgsql;

namespace OsoujiSystem.Infrastructure.Projection;

internal sealed class ReadModelCacheInvalidationTaskRepository(NpgsqlDataSource dataSource)
    : IReadModelCacheInvalidationTaskRepository
{
    public async Task EnqueueAsync(
        string projectorName,
        string cacheKey,
        ReadModelCacheInvalidationOperationKind operationKind,
        long reasonGlobalPosition,
        string? lastError,
        CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO readmodel_cache_invalidation_tasks (
                projector_name,
                cache_key,
                operation_kind,
                reason_global_position,
                retry_count,
                next_retry_at,
                last_error,
                created_at
            )
            VALUES (
                @projectorName,
                @cacheKey,
                @operationKind,
                @reasonGlobalPosition,
                0,
                now(),
                @lastError,
                now()
            )
            ON CONFLICT (projector_name, cache_key, operation_kind, reason_global_position)
            DO NOTHING;
            """,
            new
            {
                projectorName,
                cacheKey,
                operationKind = ToStorageValue(operationKind),
                reasonGlobalPosition,
                lastError
            });
    }

    public async Task<IReadOnlyList<ReadModelCacheInvalidationTask>> ListDueAsync(int batchSize, DateTimeOffset utcNow, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<ReadModelCacheInvalidationTaskRow>(
            """
            SELECT task_id AS TaskId,
                   projector_name AS ProjectorName,
                   cache_key AS CacheKey,
                   operation_kind AS OperationKind,
                   reason_global_position AS ReasonGlobalPosition,
                   retry_count AS RetryCount
            FROM readmodel_cache_invalidation_tasks
            WHERE resolved_at IS NULL
              AND next_retry_at <= @utcNow
            ORDER BY next_retry_at ASC
            LIMIT @batchSize;
            """,
            new { utcNow, batchSize });

        return rows
            .Select(x => new ReadModelCacheInvalidationTask(
                x.TaskId,
                x.ProjectorName,
                x.CacheKey,
                ParseStorageValue(x.OperationKind),
                x.ReasonGlobalPosition,
                x.RetryCount))
            .ToArray();
    }

    public async Task MarkResolvedAsync(Guid taskId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            UPDATE readmodel_cache_invalidation_tasks
            SET resolved_at = now(),
                last_error = NULL
            WHERE task_id = @taskId;
            """,
            new { taskId });
    }

    public async Task MarkFailedAsync(Guid taskId, int nextRetryCount, DateTimeOffset nextRetryAt, string lastError, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            UPDATE readmodel_cache_invalidation_tasks
            SET retry_count = @nextRetryCount,
                next_retry_at = @nextRetryAt,
                last_error = @lastError
            WHERE task_id = @taskId;
            """,
            new { taskId, nextRetryCount, nextRetryAt, lastError });
    }

    private static string ToStorageValue(ReadModelCacheInvalidationOperationKind operationKind)
        => operationKind switch
        {
            ReadModelCacheInvalidationOperationKind.Remove => "remove",
            ReadModelCacheInvalidationOperationKind.IncrementNamespace => "increment_namespace",
            _ => throw new ArgumentOutOfRangeException(nameof(operationKind), operationKind, null)
        };

    private static ReadModelCacheInvalidationOperationKind ParseStorageValue(string operationKind)
        => operationKind switch
        {
            "remove" => ReadModelCacheInvalidationOperationKind.Remove,
            "increment_namespace" => ReadModelCacheInvalidationOperationKind.IncrementNamespace,
            _ => throw new InvalidOperationException($"Unsupported read model cache invalidation operation: {operationKind}")
        };

    private sealed record ReadModelCacheInvalidationTaskRow(
        Guid TaskId,
        string ProjectorName,
        string CacheKey,
        string OperationKind,
        long ReasonGlobalPosition,
        int RetryCount);
}

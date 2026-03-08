using Dapper;
using Npgsql;

namespace OsoujiSystem.Infrastructure.Cache;

internal sealed class CacheInvalidationTaskRepository(NpgsqlDataSource dataSource) : ICacheInvalidationTaskRepository
{
    public async Task EnqueueAsync(string cacheKey, long reasonGlobalPosition, string? lastError, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO cache_invalidation_tasks (
                cache_key,
                reason_global_position,
                retry_count,
                next_retry_at,
                last_error,
                created_at
            )
            VALUES (
                @cacheKey,
                @reasonGlobalPosition,
                0,
                now(),
                @lastError,
                now()
            )
            ON CONFLICT (cache_key, reason_global_position)
            DO NOTHING;
            """,
            new { cacheKey, reasonGlobalPosition, lastError });
    }

    public async Task<IReadOnlyList<CacheInvalidationTask>> ListDueAsync(int batchSize, DateTimeOffset utcNow, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<CacheInvalidationTask>(
            """
            SELECT task_id AS TaskId,
                   cache_key AS CacheKey,
                   reason_global_position AS ReasonGlobalPosition,
                   retry_count AS RetryCount
            FROM cache_invalidation_tasks
            WHERE resolved_at IS NULL
              AND next_retry_at <= @utcNow
            ORDER BY next_retry_at ASC
            LIMIT @batchSize;
            """,
            new { utcNow, batchSize });

        return rows.ToArray();
    }

    public async Task MarkResolvedAsync(Guid taskId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            UPDATE cache_invalidation_tasks
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
            UPDATE cache_invalidation_tasks
            SET retry_count = @nextRetryCount,
                next_retry_at = @nextRetryAt,
                last_error = @lastError
            WHERE task_id = @taskId;
            """,
            new { taskId, nextRetryCount, nextRetryAt, lastError });
    }
}

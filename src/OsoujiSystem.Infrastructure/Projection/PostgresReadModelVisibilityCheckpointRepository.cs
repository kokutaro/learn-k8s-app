using Dapper;
using Npgsql;

namespace OsoujiSystem.Infrastructure.Projection;

internal sealed class PostgresReadModelVisibilityCheckpointRepository(NpgsqlDataSource dataSource)
    : IReadModelVisibilityCheckpointRepository
{
    public async Task<ReadModelVisibilityCheckpointState> GetStateAsync(string projectorName, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await EnsureVisibilityCheckpointRowAsync(connection, projectorName);

        var state = await connection.QuerySingleAsync<ReadModelVisibilityCheckpointState>(
            """
            SELECT pc.projector_name AS ProjectorName,
                   pc.last_global_position AS ProjectionCheckpoint,
                   rvc.last_visible_global_position AS VisibilityCheckpoint,
                   (
                       SELECT MIN(reason_global_position)
                       FROM readmodel_cache_invalidation_tasks tasks
                       WHERE tasks.projector_name = pc.projector_name
                         AND tasks.resolved_at IS NULL
                   ) AS MinPendingInvalidationPosition
            FROM projection_checkpoints pc
            INNER JOIN readmodel_visibility_checkpoints rvc
                ON rvc.projector_name = pc.projector_name
            WHERE pc.projector_name = @projectorName;
            """,
            new { projectorName });

        return state;
    }

    public async Task UpsertVisibilityCheckpointAsync(string projectorName, long lastVisibleGlobalPosition, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO readmodel_visibility_checkpoints (
                projector_name,
                last_visible_global_position,
                updated_at
            )
            VALUES (
                @projectorName,
                @lastVisibleGlobalPosition,
                now()
            )
            ON CONFLICT (projector_name)
            DO UPDATE SET
                last_visible_global_position = EXCLUDED.last_visible_global_position,
                updated_at = now();
            """,
            new { projectorName, lastVisibleGlobalPosition });
    }

    private static Task EnsureVisibilityCheckpointRowAsync(NpgsqlConnection connection, string projectorName)
        => connection.ExecuteAsync(
            """
            INSERT INTO readmodel_visibility_checkpoints (
                projector_name,
                last_visible_global_position,
                updated_at
            )
            VALUES (
                @projectorName,
                0,
                now()
            )
            ON CONFLICT (projector_name) DO NOTHING;
            """,
            new { projectorName });
}

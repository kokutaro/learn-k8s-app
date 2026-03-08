using Dapper;
using Npgsql;

namespace OsoujiSystem.Infrastructure.Messaging;

internal sealed class ConsumerProcessedEventRepository(NpgsqlDataSource dataSource) : IConsumerProcessedEventRepository
{
    public async Task<bool> IsProcessedAsync(string consumerName, Guid eventId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var count = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1)
            FROM consumer_processed_events
            WHERE consumer_name = @consumerName
              AND event_id = @eventId;
            """,
            new { consumerName, eventId });

        return count > 0;
    }

    public async Task MarkProcessedAsync(string consumerName, Guid eventId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO consumer_processed_events (consumer_name, event_id, processed_at)
            VALUES (@consumerName, @eventId, now())
            ON CONFLICT (consumer_name, event_id) DO NOTHING;
            """,
            new { consumerName, eventId });
    }
}

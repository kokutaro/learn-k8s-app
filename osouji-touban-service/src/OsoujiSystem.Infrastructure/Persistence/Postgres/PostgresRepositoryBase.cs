using Dapper;
using Npgsql;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal abstract class PostgresRepositoryBase
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IEventWriteContextAccessor _eventWriteContextAccessor;
    protected readonly ITransactionContextAccessor TransactionContextAccessor;

    protected PostgresRepositoryBase(
        NpgsqlDataSource dataSource,
        ITransactionContextAccessor transactionContextAccessor,
        IEventWriteContextAccessor eventWriteContextAccessor)
    {
        _dataSource = dataSource;
        TransactionContextAccessor = transactionContextAccessor;
        _eventWriteContextAccessor = eventWriteContextAccessor;
    }

    protected async Task<T> ExecuteReadAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction?, Task<T>> action,
        CancellationToken ct)
    {
        if (TransactionContextAccessor.HasActiveTransaction)
        {
            return await action(TransactionContextAccessor.Connection!, TransactionContextAccessor.Transaction);
        }

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        return await action(connection, null);
    }

    protected async Task ExecuteWriteAsync(
        Func<NpgsqlConnection, NpgsqlTransaction, Task> action,
        CancellationToken ct)
    {
        if (TransactionContextAccessor.HasActiveTransaction)
        {
            await action(TransactionContextAccessor.Connection!, TransactionContextAccessor.Transaction!);
            return;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            await action(connection, transaction);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    protected static Task<long> GetCurrentVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid streamId,
        string streamType)
    {
        return connection.QuerySingleAsync<long>(
            """
            SELECT COALESCE(MAX(stream_version), 0)
            FROM event_store_events
            WHERE stream_id = @streamId AND stream_type = @streamType;
            """,
            new { streamId, streamType },
            transaction: transaction);
    }

    protected async Task AppendEventsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid streamId,
        string streamType,
        long expectedVersion,
        IReadOnlyList<OsoujiSystem.Domain.Abstractions.IDomainEvent> domainEvents)
    {
        for (var i = 0; i < domainEvents.Count; i++)
        {
            var domainEvent = domainEvents[i];
            var streamVersion = expectedVersion + i + 1;
            var eventId = Guid.NewGuid();

            await connection.ExecuteAsync(
                """
                INSERT INTO event_store_events (
                    event_id,
                    stream_id,
                    stream_type,
                    stream_version,
                    event_type,
                    event_schema_version,
                    payload,
                    metadata,
                    occurred_at
                )
                VALUES (
                    @eventId,
                    @streamId,
                    @streamType,
                    @streamVersion,
                    @eventType,
                    1,
                    CAST(@payload AS jsonb),
                    '{}'::jsonb,
                    @occurredAt
                );
                """,
                new
                {
                    eventId,
                    streamId,
                    streamType,
                    streamVersion,
                    eventType = domainEvent.GetType().Name,
                    payload = EventStoreDocuments.SerializeEvent(domainEvent),
                    occurredAt = domainEvent.OccurredAt
                },
                transaction: transaction);

            _eventWriteContextAccessor.Register(domainEvent, eventId);
        }
    }

    protected static Task UpsertSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid streamId,
        string streamType,
        long lastIncludedVersion,
        string snapshotPayload)
    {
        return connection.ExecuteAsync(
            """
            INSERT INTO event_store_snapshots (stream_id, stream_type, last_included_version, snapshot_payload, updated_at)
            VALUES (@streamId, @streamType, @lastIncludedVersion, CAST(@snapshotPayload AS jsonb), now())
            ON CONFLICT (stream_id)
            DO UPDATE SET
                stream_type = EXCLUDED.stream_type,
                last_included_version = EXCLUDED.last_included_version,
                snapshot_payload = EXCLUDED.snapshot_payload,
                updated_at = now();
            """,
            new
            {
                streamId,
                streamType,
                lastIncludedVersion,
                snapshotPayload
            },
            transaction: transaction);
    }
}

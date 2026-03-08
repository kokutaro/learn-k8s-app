using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Npgsql;
using OsoujiSystem.Domain.Abstractions;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal abstract class PostgresRepositoryBase(
    NpgsqlDataSource dataSource,
    ITransactionContextAccessor transactionContextAccessor,
    IEventWriteContextAccessor eventWriteContextAccessor)
{
    private static readonly JsonSerializerOptions BatchJsonOptions = new(JsonSerializerDefaults.Web);
    protected readonly ITransactionContextAccessor TransactionContextAccessor = transactionContextAccessor;

  protected async Task<T> ExecuteReadAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction?, Task<T>> action,
        CancellationToken ct)
    {
        if (TransactionContextAccessor.HasActiveTransaction)
        {
            return await action(TransactionContextAccessor.Connection!, TransactionContextAccessor.Transaction);
        }

        await using var connection = await dataSource.OpenConnectionAsync(ct);
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

        await using var connection = await dataSource.OpenConnectionAsync(ct);
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
        IReadOnlyList<IDomainEvent> domainEvents)
    {
        if (domainEvents.Count == 0)
        {
            return;
        }

        var pendingEvents = new PendingEventWrite[domainEvents.Count];
        for (var i = 0; i < domainEvents.Count; i++)
        {
            var domainEvent = domainEvents[i];
            pendingEvents[i] = new PendingEventWrite(
                domainEvent,
                new EventInsertRow(
                    i,
                    Guid.NewGuid(),
                    streamId,
                    streamType,
                    expectedVersion + i + 1,
                    domainEvent.GetType().Name,
                    EventStoreDocuments.SerializeEvent(domainEvent),
                    domainEvent.OccurredAt));
        }

        await connection.ExecuteAsync(
            """
            WITH event_rows AS (
                SELECT ordinal,
                       event_id,
                       stream_id,
                       stream_type,
                       stream_version,
                       event_type,
                       payload,
                       occurred_at
                FROM jsonb_to_recordset(CAST(@rows AS jsonb)) AS x(
                    ordinal integer,
                    event_id uuid,
                    stream_id uuid,
                    stream_type text,
                    stream_version bigint,
                    event_type text,
                    payload text,
                    occurred_at timestamptz
                )
            )
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
            SELECT event_id,
                   stream_id,
                   stream_type,
                   stream_version,
                   event_type,
                   1,
                   CAST(payload AS jsonb),
                   '{}'::jsonb,
                   occurred_at
            FROM event_rows
            ORDER BY ordinal;
            """,
            new
            {
                rows = JsonSerializer.Serialize(
                    pendingEvents.Select(x => x.Row).ToArray(),
                    BatchJsonOptions)
            },
            transaction: transaction);

        foreach (var pendingEvent in pendingEvents)
        {
            eventWriteContextAccessor.Register(pendingEvent.DomainEvent, pendingEvent.Row.EventId);
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

    private sealed record PendingEventWrite(IDomainEvent DomainEvent, EventInsertRow Row);

    private sealed record EventInsertRow(
        [property: JsonPropertyName("ordinal")] int Ordinal,
        [property: JsonPropertyName("event_id")] Guid EventId,
        [property: JsonPropertyName("stream_id")] Guid StreamId,
        [property: JsonPropertyName("stream_type")] string StreamType,
        [property: JsonPropertyName("stream_version")] long StreamVersion,
        [property: JsonPropertyName("event_type")] string EventType,
        [property: JsonPropertyName("payload")] string Payload,
        [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);
}

using Dapper;
using Npgsql;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.UserManagement;
using OsoujiSystem.Domain.Entities.UserManagement.ValueObjects;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class EventStoreManagedUserRepository(
    NpgsqlDataSource dataSource,
    ITransactionContextAccessor transactionContextAccessor,
    IEventWriteContextAccessor eventWriteContextAccessor)
    : PostgresRepositoryBase(dataSource, transactionContextAccessor, eventWriteContextAccessor), IManagedUserRepository
{
    public Task<LoadedAggregate<ManagedUser>?> FindByIdAsync(UserId userId, CancellationToken ct)
        => ExecuteReadAsync<LoadedAggregate<ManagedUser>?>(async (connection, transaction) =>
        {
            var snapshot = await connection.QuerySingleOrDefaultAsync<SnapshotRow>(
                """
                SELECT last_included_version AS Version, snapshot_payload::text AS Payload
                FROM event_store_snapshots
                WHERE stream_id = @streamId AND stream_type = @streamType;
                """,
                new { streamId = userId.Value, streamType = EventStoreDocuments.ManagedUserStreamType },
                transaction: transaction);

            if (snapshot is null)
            {
                return null;
            }

            var aggregate = EventStoreDocuments.DeserializeManagedUserSnapshot(userId.Value, snapshot.Payload);
            return new LoadedAggregate<ManagedUser>(aggregate, new AggregateVersion(snapshot.Version));
        }, ct);

    public Task<LoadedAggregate<ManagedUser>?> FindByEmployeeNumberAsync(EmployeeNumber employeeNumber, CancellationToken ct)
        => ExecuteReadAsync<LoadedAggregate<ManagedUser>?>(async (connection, transaction) =>
        {
            var snapshot = await connection.QuerySingleOrDefaultAsync<SnapshotByStreamRow>(
                """
                SELECT stream_id AS StreamId,
                       last_included_version AS Version,
                       snapshot_payload::text AS Payload
                FROM event_store_snapshots
                WHERE stream_type = @streamType
                  AND snapshot_payload->>'employeeNumber' = @employeeNumber;
                """,
                new
                {
                    streamType = EventStoreDocuments.ManagedUserStreamType,
                    employeeNumber = employeeNumber.Value
                },
                transaction: transaction);

            if (snapshot is null)
            {
                return null;
            }

            var aggregate = EventStoreDocuments.DeserializeManagedUserSnapshot(snapshot.StreamId, snapshot.Payload);
            return new LoadedAggregate<ManagedUser>(aggregate, new AggregateVersion(snapshot.Version));
        }, ct);

    public Task<LoadedAggregate<ManagedUser>?> FindByIdentityLinkAsync(
        IdentityProviderKey identityProviderKey,
        IdentitySubject identitySubject,
        CancellationToken ct)
        => ExecuteReadAsync<LoadedAggregate<ManagedUser>?>(async (connection, transaction) =>
        {
            var snapshot = await connection.QuerySingleOrDefaultAsync<SnapshotByStreamRow>(
                """
                SELECT stream_id AS StreamId,
                       last_included_version AS Version,
                       snapshot_payload::text AS Payload
                FROM event_store_snapshots
                WHERE stream_type = @streamType
                  AND EXISTS (
                      SELECT 1
                      FROM jsonb_array_elements(snapshot_payload->'authIdentityLinks') AS identity_link
                      WHERE identity_link->>'identityProviderKey' = @identityProviderKey
                        AND identity_link->>'identitySubject' = @identitySubject
                  );
                """,
                new
                {
                    streamType = EventStoreDocuments.ManagedUserStreamType,
                    identityProviderKey = identityProviderKey.Value,
                    identitySubject = identitySubject.Value
                },
                transaction: transaction);

            if (snapshot is null)
            {
                return null;
            }

            var aggregate = EventStoreDocuments.DeserializeManagedUserSnapshot(snapshot.StreamId, snapshot.Payload);
            return new LoadedAggregate<ManagedUser>(aggregate, new AggregateVersion(snapshot.Version));
        }, ct);

    public Task AddAsync(ManagedUser aggregate, CancellationToken ct)
        => ExecuteWriteAsync(async (connection, transaction) =>
        {
            try
            {
                var streamId = aggregate.Id.Value;
                var currentVersion = await GetCurrentVersionAsync(connection, transaction, streamId, EventStoreDocuments.ManagedUserStreamType);
                if (currentVersion > 0)
                {
                    throw new RepositoryDuplicateException($"ManagedUser stream already exists: {aggregate.Id}");
                }

                var domainEvents = aggregate.DomainEvents.ToArray();
                await AppendEventsAsync(connection, transaction, streamId, EventStoreDocuments.ManagedUserStreamType, 0, domainEvents);

                var targetVersion = Math.Max(1, domainEvents.Length);
                await UpsertSnapshotAsync(
                    connection,
                    transaction,
                    streamId,
                    EventStoreDocuments.ManagedUserStreamType,
                    targetVersion,
                    EventStoreDocuments.SerializeSnapshot(aggregate));
            }
            catch (Exception ex)
            {
                throw PostgresExceptionMapper.Map(ex);
            }
        }, ct);

    public Task SaveAsync(ManagedUser aggregate, AggregateVersion expectedVersion, CancellationToken ct)
        => ExecuteWriteAsync(async (connection, transaction) =>
        {
            try
            {
                var streamId = aggregate.Id.Value;
                var currentVersion = await GetCurrentVersionAsync(connection, transaction, streamId, EventStoreDocuments.ManagedUserStreamType);
                if (currentVersion != expectedVersion.Value)
                {
                    throw new RepositoryConcurrencyException(
                        $"Expected version {expectedVersion.Value} but found {currentVersion} for stream {aggregate.Id}.");
                }

                var domainEvents = aggregate.DomainEvents.ToArray();
                await AppendEventsAsync(connection, transaction, streamId, EventStoreDocuments.ManagedUserStreamType, expectedVersion.Value, domainEvents);

                var targetVersion = expectedVersion.Value + domainEvents.Length;
                await UpsertSnapshotAsync(
                    connection,
                    transaction,
                    streamId,
                    EventStoreDocuments.ManagedUserStreamType,
                    targetVersion,
                    EventStoreDocuments.SerializeSnapshot(aggregate));
            }
            catch (Exception ex)
            {
                throw PostgresExceptionMapper.Map(ex);
            }
        }, ct);

    private sealed record SnapshotRow(long Version, string Payload);
    private sealed record SnapshotByStreamRow(Guid StreamId, long Version, string Payload);
}

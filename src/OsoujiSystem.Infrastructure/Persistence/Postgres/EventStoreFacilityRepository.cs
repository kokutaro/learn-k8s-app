using Dapper;
using Npgsql;
using OsoujiSystem.Domain.Entities.Facilities;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Infrastructure.Serialization;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class EventStoreFacilityRepository(
    NpgsqlDataSource dataSource,
    ITransactionContextAccessor transactionContextAccessor,
    IEventWriteContextAccessor eventWriteContextAccessor,
    EventStoreDocuments eventStoreDocuments,
    InfrastructureJsonSerializer jsonSerializer)
    : PostgresRepositoryBase(dataSource, transactionContextAccessor, eventWriteContextAccessor, eventStoreDocuments, jsonSerializer), IFacilityRepository
{
    private readonly EventStoreDocuments _eventStoreDocuments = eventStoreDocuments;

    public Task<LoadedAggregate<Facility>?> FindByIdAsync(FacilityId facilityId, CancellationToken ct)
        => ExecuteReadAsync<LoadedAggregate<Facility>?>(async (connection, transaction) =>
        {
            var snapshot = await connection.QuerySingleOrDefaultAsync<SnapshotRow>(
                """
                SELECT last_included_version AS Version, snapshot_payload::text AS Payload
                FROM event_store_snapshots
                WHERE stream_id = @streamId AND stream_type = @streamType;
                """,
                new { streamId = facilityId.Value, streamType = EventStoreDocuments.FacilityStreamType },
                transaction: transaction);

            if (snapshot is null)
            {
                return null;
            }

            var aggregate = _eventStoreDocuments.DeserializeFacilitySnapshot(facilityId.Value, snapshot.Payload);
            return new LoadedAggregate<Facility>(aggregate, new AggregateVersion(snapshot.Version));
        }, ct);

    public Task<LoadedAggregate<Facility>?> FindByCodeAsync(FacilityCode facilityCode, CancellationToken ct)
        => ExecuteReadAsync<LoadedAggregate<Facility>?>(async (connection, transaction) =>
        {
            var snapshot = await connection.QuerySingleOrDefaultAsync<SnapshotByStreamRow>(
                """
                SELECT stream_id AS StreamId,
                       last_included_version AS Version,
                       snapshot_payload::text AS Payload
                FROM event_store_snapshots
                WHERE stream_type = @streamType
                  AND snapshot_payload->>'facilityCode' = @facilityCode;
                """,
                new
                {
                    streamType = EventStoreDocuments.FacilityStreamType,
                    facilityCode = facilityCode.Value
                },
                transaction: transaction);

            if (snapshot is null)
            {
                return null;
            }

            var aggregate = _eventStoreDocuments.DeserializeFacilitySnapshot(snapshot.StreamId, snapshot.Payload);
            return new LoadedAggregate<Facility>(aggregate, new AggregateVersion(snapshot.Version));
        }, ct);

    public Task AddAsync(Facility aggregate, CancellationToken ct)
        => ExecuteWriteAsync(async (connection, transaction) =>
        {
            try
            {
                var streamId = aggregate.Id.Value;
                var currentVersion = await GetCurrentVersionAsync(connection, transaction, streamId, EventStoreDocuments.FacilityStreamType);
                if (currentVersion > 0)
                {
                    throw new RepositoryDuplicateException($"Facility stream already exists: {aggregate.Id}");
                }

                var domainEvents = aggregate.DomainEvents.ToArray();
                await AppendEventsAsync(connection, transaction, streamId, EventStoreDocuments.FacilityStreamType, 0, domainEvents);

                var targetVersion = Math.Max(1, domainEvents.Length);
                await UpsertSnapshotAsync(
                    connection,
                    transaction,
                    streamId,
                    EventStoreDocuments.FacilityStreamType,
                    targetVersion,
                    _eventStoreDocuments.SerializeSnapshot(aggregate));
            }
            catch (Exception ex)
            {
                throw PostgresExceptionMapper.Map(ex);
            }
        }, ct);

    public Task SaveAsync(Facility aggregate, AggregateVersion expectedVersion, CancellationToken ct)
        => ExecuteWriteAsync(async (connection, transaction) =>
        {
            try
            {
                var streamId = aggregate.Id.Value;
                var currentVersion = await GetCurrentVersionAsync(connection, transaction, streamId, EventStoreDocuments.FacilityStreamType);
                if (currentVersion != expectedVersion.Value)
                {
                    throw new RepositoryConcurrencyException(
                        $"Expected version {expectedVersion.Value} but found {currentVersion} for stream {aggregate.Id}.");
                }

                var domainEvents = aggregate.DomainEvents.ToArray();
                await AppendEventsAsync(connection, transaction, streamId, EventStoreDocuments.FacilityStreamType, expectedVersion.Value, domainEvents);

                var targetVersion = expectedVersion.Value + domainEvents.Length;
                await UpsertSnapshotAsync(
                    connection,
                    transaction,
                    streamId,
                    EventStoreDocuments.FacilityStreamType,
                    targetVersion,
                    _eventStoreDocuments.SerializeSnapshot(aggregate));
            }
            catch (Exception ex)
            {
                throw PostgresExceptionMapper.Map(ex);
            }
        }, ct);

    private sealed record SnapshotRow(long Version, string Payload);
    private sealed record SnapshotByStreamRow(Guid StreamId, long Version, string Payload);
}

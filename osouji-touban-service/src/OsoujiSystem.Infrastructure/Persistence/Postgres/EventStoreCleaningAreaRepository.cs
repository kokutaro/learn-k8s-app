using Dapper;
using Npgsql;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class EventStoreCleaningAreaRepository : PostgresRepositoryBase, ICleaningAreaRepository
{
    public EventStoreCleaningAreaRepository(
        NpgsqlDataSource dataSource,
        ITransactionContextAccessor transactionContextAccessor)
        : base(dataSource, transactionContextAccessor)
    {
    }

    public Task<LoadedAggregate<CleaningArea>?> FindByIdAsync(CleaningAreaId areaId, CancellationToken ct)
        => ExecuteReadAsync<LoadedAggregate<CleaningArea>?>(async (connection, transaction) =>
        {
            var snapshot = await connection.QuerySingleOrDefaultAsync<SnapshotRow>(
                """
                SELECT last_included_version AS Version, snapshot_payload::text AS Payload
                FROM event_store_snapshots
                WHERE stream_id = @streamId AND stream_type = @streamType;
                """,
                new { streamId = areaId.Value, streamType = EventStoreDocuments.CleaningAreaStreamType },
                transaction: transaction);

            if (snapshot is null)
            {
                return null;
            }

            var aggregate = EventStoreDocuments.DeserializeCleaningAreaSnapshot(areaId.Value, snapshot.Payload);
            return new LoadedAggregate<CleaningArea>(aggregate, new AggregateVersion(snapshot.Version));
        }, ct);

    public async Task<LoadedAggregate<CleaningArea>?> FindByUserIdAsync(UserId userId, CancellationToken ct)
    {
        var projected = await ExecuteReadAsync(async (connection, transaction) =>
        {
            return await connection.QuerySingleOrDefaultAsync<Guid?>(
                """
                SELECT area_id
                FROM projection_area_members
                WHERE user_id = @userId AND is_active = true;
                """,
                new { userId = userId.Value },
                transaction: transaction);
        }, ct);

        if (projected.HasValue)
        {
            return await FindByIdAsync(new CleaningAreaId(projected.Value), ct);
        }

        return await ExecuteReadAsync<LoadedAggregate<CleaningArea>?>(async (connection, transaction) =>
        {
            var rows = await connection.QueryAsync<SnapshotScanRow>(
                """
                SELECT stream_id AS StreamId, last_included_version AS Version, snapshot_payload::text AS Payload
                FROM event_store_snapshots
                WHERE stream_type = @streamType;
                """,
                new { streamType = EventStoreDocuments.CleaningAreaStreamType },
                transaction: transaction);

            foreach (var row in rows)
            {
                var aggregate = EventStoreDocuments.DeserializeCleaningAreaSnapshot(row.StreamId, row.Payload);
                if (aggregate.Members.Any(x => x.UserId == userId))
                {
                    return new LoadedAggregate<CleaningArea>(aggregate, new AggregateVersion(row.Version));
                }
            }

            return null;
        }, ct);
    }

    public Task<IReadOnlyList<LoadedAggregate<CleaningArea>>> ListAllAsync(CancellationToken ct)
        => ExecuteReadAsync<IReadOnlyList<LoadedAggregate<CleaningArea>>>(async (connection, transaction) =>
        {
            var rows = await connection.QueryAsync<SnapshotScanRow>(
                """
                SELECT stream_id AS StreamId, last_included_version AS Version, snapshot_payload::text AS Payload
                FROM event_store_snapshots
                WHERE stream_type = @streamType;
                """,
                new { streamType = EventStoreDocuments.CleaningAreaStreamType },
                transaction: transaction);

            return (IReadOnlyList<LoadedAggregate<CleaningArea>>)rows
                .Select(row => new LoadedAggregate<CleaningArea>(
                    EventStoreDocuments.DeserializeCleaningAreaSnapshot(row.StreamId, row.Payload),
                    new AggregateVersion(row.Version)))
                .ToArray();
        }, ct);

    public async Task<IReadOnlyList<LoadedAggregate<CleaningArea>>> ListWeekRuleDueAsync(WeekId currentWeek, CancellationToken ct)
    {
        var projectedIds = await ExecuteReadAsync(async (connection, transaction) =>
        {
            return (await connection.QueryAsync<Guid>(
                """
                SELECT area_id
                FROM projection_cleaning_areas
                WHERE pending_week_rule IS NOT NULL
                  AND (
                    (pending_week_rule->'EffectiveFromWeek'->>'Year')::int < @year
                    OR (
                        (pending_week_rule->'EffectiveFromWeek'->>'Year')::int = @year
                        AND (pending_week_rule->'EffectiveFromWeek'->>'WeekNumber')::int <= @weekNumber
                    )
                  );
                """,
                new { year = currentWeek.Year, weekNumber = currentWeek.WeekNumber },
                transaction: transaction)).ToArray();
        }, ct);

        if (projectedIds.Length > 0)
        {
            var list = new List<LoadedAggregate<CleaningArea>>();
            foreach (var areaId in projectedIds)
            {
                var loaded = await FindByIdAsync(new CleaningAreaId(areaId), ct);
                if (loaded is not null)
                {
                    list.Add(loaded.Value);
                }
            }

            return list;
        }

        var snapshots = await ListAllAsync(ct);
        return snapshots
            .Where(x => x.Aggregate.PendingWeekRule is not null && x.Aggregate.PendingWeekRule.Value.EffectiveFromWeek.CompareTo(currentWeek) <= 0)
            .ToArray();
    }

    public Task AddAsync(CleaningArea aggregate, CancellationToken ct)
        => ExecuteWriteAsync(async (connection, transaction) =>
        {
            try
            {
                var streamId = aggregate.Id.Value;
                var currentVersion = await GetCurrentVersionAsync(connection, transaction, streamId, EventStoreDocuments.CleaningAreaStreamType);
                if (currentVersion > 0)
                {
                    throw new RepositoryDuplicateException($"CleaningArea stream already exists: {aggregate.Id}");
                }

                var domainEvents = aggregate.DomainEvents.ToArray();
                await AppendEventsAsync(connection, transaction, streamId, EventStoreDocuments.CleaningAreaStreamType, 0, domainEvents);

                var targetVersion = Math.Max(1, domainEvents.Length);
                await UpsertSnapshotAsync(
                    connection,
                    transaction,
                    streamId,
                    EventStoreDocuments.CleaningAreaStreamType,
                    targetVersion,
                    EventStoreDocuments.SerializeSnapshot(aggregate));
            }
            catch (Exception ex)
            {
                throw PostgresExceptionMapper.Map(ex);
            }
        }, ct);

    public Task SaveAsync(CleaningArea aggregate, AggregateVersion expectedVersion, CancellationToken ct)
        => ExecuteWriteAsync(async (connection, transaction) =>
        {
            try
            {
                var streamId = aggregate.Id.Value;
                var currentVersion = await GetCurrentVersionAsync(connection, transaction, streamId, EventStoreDocuments.CleaningAreaStreamType);
                if (currentVersion != expectedVersion.Value)
                {
                    throw new RepositoryConcurrencyException(
                        $"Expected version {expectedVersion.Value} but found {currentVersion} for stream {aggregate.Id}.");
                }

                var domainEvents = aggregate.DomainEvents.ToArray();
                await AppendEventsAsync(connection, transaction, streamId, EventStoreDocuments.CleaningAreaStreamType, expectedVersion.Value, domainEvents);

                var targetVersion = expectedVersion.Value + domainEvents.Length;
                await UpsertSnapshotAsync(
                    connection,
                    transaction,
                    streamId,
                    EventStoreDocuments.CleaningAreaStreamType,
                    targetVersion,
                    EventStoreDocuments.SerializeSnapshot(aggregate));
            }
            catch (Exception ex)
            {
                throw PostgresExceptionMapper.Map(ex);
            }
        }, ct);

    private sealed record SnapshotRow(long Version, string Payload);
    private sealed record SnapshotScanRow(Guid StreamId, long Version, string Payload);
}

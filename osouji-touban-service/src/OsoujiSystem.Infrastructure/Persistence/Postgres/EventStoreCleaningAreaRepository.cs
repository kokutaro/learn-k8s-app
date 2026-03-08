using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;
using OsoujiSystem.Infrastructure.Cache;
using OsoujiSystem.Infrastructure.Options;
using OsoujiSystem.Infrastructure.Serialization;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class EventStoreCleaningAreaRepository(
    NpgsqlDataSource dataSource,
    ITransactionContextAccessor transactionContextAccessor,
    IEventWriteContextAccessor eventWriteContextAccessor,
    EventStoreDocuments eventStoreDocuments,
    InfrastructureJsonSerializer jsonSerializer,
    IAggregateCache cache,
    ICacheKeyFactory cacheKeyFactory,
    ICacheInvalidationTaskRepository invalidationTasks,
    IOptions<InfrastructureOptions> options) : PostgresRepositoryBase(dataSource, transactionContextAccessor, eventWriteContextAccessor, eventStoreDocuments, jsonSerializer), ICleaningAreaRepository
{
    private readonly TimeSpan _defaultTtl = TimeSpan.FromSeconds(options.Value.Redis.DefaultTtlSeconds);

  public async Task<LoadedAggregate<CleaningArea>?> FindByIdAsync(CleaningAreaId areaId, CancellationToken ct)
    {
        var latestKey = cacheKeyFactory.CleaningAreaLatest(areaId);

        try
        {
            var latest = await cache.TryGetAsync(latestKey, ct);
            if (latest is not null && long.TryParse(latest.Value.Payload, out var version))
            {
                var versionKey = cacheKeyFactory.CleaningAreaVersion(areaId, version);
                var snapshotCached = await cache.TryGetAsync(versionKey, ct);
                if (snapshotCached is not null)
                {
                    var aggregate = eventStoreDocuments.DeserializeCleaningAreaSnapshot(areaId.Value, snapshotCached.Value.Payload);
                    return new LoadedAggregate<CleaningArea>(aggregate, new AggregateVersion(snapshotCached.Value.Version));
                }
            }
        }
        catch
        {
            // Redis failure falls back to DB.
        }

        var loaded = await ExecuteReadAsync<LoadedAggregate<CleaningArea>?>(async (connection, transaction) =>
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

            var aggregate = eventStoreDocuments.DeserializeCleaningAreaSnapshot(areaId.Value, snapshot.Payload);
            return new LoadedAggregate<CleaningArea>(aggregate, new AggregateVersion(snapshot.Version));
        }, ct);

        if (loaded is not null)
        {
            await TryCacheAreaAsync(loaded.Value.Aggregate, loaded.Value.Version.Value, ct);
        }

        return loaded;
    }

    public async Task<LoadedAggregate<CleaningArea>?> FindByUserIdAsync(UserId userId, CancellationToken ct)
    {
        var userLatestKey = cacheKeyFactory.CleaningAreaUserLatest(userId);

        try
        {
            var latest = await cache.TryGetAsync(userLatestKey, ct);
            if (latest is not null)
            {
                var parts = latest.Value.Payload.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && Guid.TryParse(parts[0], out var areaGuid) && long.TryParse(parts[1], out var version))
                {
                    var areaId = new CleaningAreaId(areaGuid);
                    var versionKey = cacheKeyFactory.CleaningAreaVersion(areaId, version);
                    var cached = await cache.TryGetAsync(versionKey, ct);
                    if (cached is not null)
                    {
                        var aggregate = eventStoreDocuments.DeserializeCleaningAreaSnapshot(areaId.Value, cached.Value.Payload);
                        if (aggregate.Members.Any(x => x.UserId == userId))
                        {
                            return new LoadedAggregate<CleaningArea>(aggregate, new AggregateVersion(cached.Value.Version));
                        }

                        await TryDeleteWithRecoveryAsync(userLatestKey, cached.Value.Version, ct);
                    }
                }
            }
        }
        catch
        {
            // Redis failure falls back to DB.
        }

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
            var loaded = await FindByIdAsync(new CleaningAreaId(projected.Value), ct);
            if (loaded is not null)
            {
                await TrySetPointerAsync(
                    userLatestKey,
                    loaded.Value.Version.Value,
                    $"{loaded.Value.Aggregate.Id.Value:D}:{loaded.Value.Version.Value}",
                    ct);
            }

            return loaded;
        }

        var scanned = await ExecuteReadAsync<LoadedAggregate<CleaningArea>?>(async (connection, transaction) =>
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
                var aggregate = eventStoreDocuments.DeserializeCleaningAreaSnapshot(row.StreamId, row.Payload);
                if (aggregate.Members.Any(x => x.UserId == userId))
                {
                    return new LoadedAggregate<CleaningArea>(aggregate, new AggregateVersion(row.Version));
                }
            }

            return null;
        }, ct);

        if (scanned is not null)
        {
            await TryCacheAreaAsync(scanned.Value.Aggregate, scanned.Value.Version.Value, ct);
            await TrySetPointerAsync(
                userLatestKey,
                scanned.Value.Version.Value,
                $"{scanned.Value.Aggregate.Id.Value:D}:{scanned.Value.Version.Value}",
                ct);
        }

        return scanned;
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

            return rows
                .Select(row => new LoadedAggregate<CleaningArea>(
                    eventStoreDocuments.DeserializeCleaningAreaSnapshot(row.StreamId, row.Payload),
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
            var rows = await ExecuteReadAsync(async (connection, transaction) =>
            {
                return (await connection.QueryAsync<SnapshotScanRow>(
                    """
                    SELECT stream_id AS StreamId,
                           last_included_version AS Version,
                           snapshot_payload::text AS Payload
                    FROM event_store_snapshots
                    WHERE stream_type = @streamType
                      AND stream_id = ANY(@streamIds);
                    """,
                    new
                    {
                        streamType = EventStoreDocuments.CleaningAreaStreamType,
                        streamIds = projectedIds
                    },
                    transaction: transaction)).ToArray();
            }, ct);

            var rowMap = rows.ToDictionary(row => row.StreamId);
            var list = new List<LoadedAggregate<CleaningArea>>(rows.Length);
            foreach (var areaId in projectedIds)
            {
                if (!rowMap.TryGetValue(areaId, out var row))
                {
                    continue;
                }

                var loaded = new LoadedAggregate<CleaningArea>(
                    eventStoreDocuments.DeserializeCleaningAreaSnapshot(row.StreamId, row.Payload),
                    new AggregateVersion(row.Version));
                list.Add(loaded);
                await TryCacheAreaAsync(loaded.Aggregate, loaded.Version.Value, ct);
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
                    eventStoreDocuments.SerializeSnapshot(aggregate));

                await TryCacheAreaAsync(aggregate, targetVersion, ct);
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
                    eventStoreDocuments.SerializeSnapshot(aggregate));

                await TryCacheAreaAsync(aggregate, targetVersion, ct);

                var oldKey = cacheKeyFactory.CleaningAreaVersion(aggregate.Id, expectedVersion.Value);
                await TryDeleteWithRecoveryAsync(oldKey, targetVersion, ct);
            }
            catch (Exception ex)
            {
                throw PostgresExceptionMapper.Map(ex);
            }
        }, ct);

    private async Task TryCacheAreaAsync(CleaningArea aggregate, long version, CancellationToken ct)
    {
        try
        {
            var snapshot = eventStoreDocuments.SerializeSnapshot(aggregate);
            var versionKey = cacheKeyFactory.CleaningAreaVersion(aggregate.Id, version);
            await cache.SetAsync(versionKey, version, snapshot, _defaultTtl, ct);
            await cache.SetAsync(cacheKeyFactory.CleaningAreaLatest(aggregate.Id), version, version.ToString(), _defaultTtl, ct);

            foreach (var member in aggregate.Members)
            {
                var userLatestKey = cacheKeyFactory.CleaningAreaUserLatest(member.UserId);
                await cache.SetAsync(userLatestKey, version, $"{aggregate.Id.Value:D}:{version}", _defaultTtl, ct);
            }
        }
        catch
        {
            // Best-effort cache update.
        }
    }

    private async Task TryDeleteWithRecoveryAsync(string cacheKey, long reasonGlobalPosition, CancellationToken ct)
    {
        try
        {
            await cache.DeleteAsync(cacheKey, ct);
        }
        catch (Exception ex)
        {
            await invalidationTasks.EnqueueAsync(cacheKey, reasonGlobalPosition, ex.Message, ct);
        }
    }

    private async Task TrySetPointerAsync(string key, long version, string payload, CancellationToken ct)
    {
        try
        {
            await cache.SetAsync(key, version, payload, _defaultTtl, ct);
        }
        catch
        {
            // Best-effort pointer update.
        }
    }

    private sealed record SnapshotRow(long Version, string Payload);
    private sealed record SnapshotScanRow(Guid StreamId, long Version, string Payload);
}

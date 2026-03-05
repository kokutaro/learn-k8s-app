using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;
using OsoujiSystem.Infrastructure.Cache;
using OsoujiSystem.Infrastructure.Options;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class EventStoreWeeklyDutyPlanRepository : PostgresRepositoryBase, IWeeklyDutyPlanRepository
{
    private readonly IAggregateCache _cache;
    private readonly ICacheKeyFactory _cacheKeyFactory;
    private readonly ICacheInvalidationTaskRepository _invalidationTasks;
    private readonly TimeSpan _defaultTtl;

    public EventStoreWeeklyDutyPlanRepository(
        NpgsqlDataSource dataSource,
        ITransactionContextAccessor transactionContextAccessor,
        IAggregateCache cache,
        ICacheKeyFactory cacheKeyFactory,
        ICacheInvalidationTaskRepository invalidationTasks,
        IOptions<InfrastructureOptions> options)
        : base(dataSource, transactionContextAccessor)
    {
        _cache = cache;
        _cacheKeyFactory = cacheKeyFactory;
        _invalidationTasks = invalidationTasks;
        _defaultTtl = TimeSpan.FromSeconds(options.Value.Redis.DefaultTtlSeconds);
    }

    public async Task<LoadedAggregate<WeeklyDutyPlan>?> FindByIdAsync(WeeklyDutyPlanId planId, CancellationToken ct)
    {
        try
        {
            var latest = await _cache.TryGetAsync(_cacheKeyFactory.WeeklyPlanLatest(planId), ct);
            if (latest is not null && long.TryParse(latest.Value.Payload, out var version))
            {
                var versionKey = _cacheKeyFactory.WeeklyPlanVersion(planId, version);
                var snapshotCached = await _cache.TryGetAsync(versionKey, ct);
                if (snapshotCached is not null)
                {
                    var aggregate = EventStoreDocuments.DeserializeWeeklyDutyPlanSnapshot(planId.Value, snapshotCached.Value.Payload);
                    return new LoadedAggregate<WeeklyDutyPlan>(aggregate, new AggregateVersion(snapshotCached.Value.Version));
                }
            }
        }
        catch
        {
            // Redis failure falls back to DB.
        }

        var loaded = await ExecuteReadAsync<LoadedAggregate<WeeklyDutyPlan>?>(async (connection, transaction) =>
        {
            var snapshot = await connection.QuerySingleOrDefaultAsync<SnapshotRow>(
                """
                SELECT last_included_version AS Version, snapshot_payload::text AS Payload
                FROM event_store_snapshots
                WHERE stream_id = @streamId AND stream_type = @streamType;
                """,
                new { streamId = planId.Value, streamType = EventStoreDocuments.WeeklyDutyPlanStreamType },
                transaction: transaction);

            if (snapshot is null)
            {
                return null;
            }

            var aggregate = EventStoreDocuments.DeserializeWeeklyDutyPlanSnapshot(planId.Value, snapshot.Payload);
            return new LoadedAggregate<WeeklyDutyPlan>(aggregate, new AggregateVersion(snapshot.Version));
        }, ct);

        if (loaded is not null)
        {
            await TryCachePlanAsync(loaded.Value.Aggregate, loaded.Value.Version.Value, ct);
        }

        return loaded;
    }

    public async Task<LoadedAggregate<WeeklyDutyPlan>?> FindByAreaAndWeekAsync(
        CleaningAreaId areaId,
        WeekId weekId,
        CancellationToken ct)
    {
        var areaWeekKey = _cacheKeyFactory.WeeklyPlanAreaWeekLatest(areaId, weekId);

        try
        {
            var latest = await _cache.TryGetAsync(areaWeekKey, ct);
            if (latest is not null)
            {
                var parts = latest.Value.Payload.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && Guid.TryParse(parts[0], out var planGuid) && long.TryParse(parts[1], out var version))
                {
                    var planId = new WeeklyDutyPlanId(planGuid);
                    var versionKey = _cacheKeyFactory.WeeklyPlanVersion(planId, version);
                    var cached = await _cache.TryGetAsync(versionKey, ct);
                    if (cached is not null)
                    {
                        var aggregate = EventStoreDocuments.DeserializeWeeklyDutyPlanSnapshot(planId.Value, cached.Value.Payload);
                        if (aggregate.AreaId == areaId && aggregate.WeekId.Equals(weekId))
                        {
                            return new LoadedAggregate<WeeklyDutyPlan>(aggregate, new AggregateVersion(cached.Value.Version));
                        }

                        await TryDeleteWithRecoveryAsync(areaWeekKey, cached.Value.Version, ct);
                    }
                }
            }
        }
        catch
        {
            // Redis failure falls back to DB.
        }

        var projectedPlanId = await ExecuteReadAsync(async (connection, transaction) =>
        {
            return await connection.QuerySingleOrDefaultAsync<Guid?>(
                """
                SELECT plan_id
                FROM projection_weekly_plans
                WHERE area_id = @areaId
                  AND week_year = @year
                  AND week_number = @weekNumber;
                """,
                new { areaId = areaId.Value, year = weekId.Year, weekNumber = weekId.WeekNumber },
                transaction: transaction);
        }, ct);

        if (projectedPlanId.HasValue)
        {
            var loaded = await FindByIdAsync(new WeeklyDutyPlanId(projectedPlanId.Value), ct);
            if (loaded is not null)
            {
                await TrySetPointerAsync(areaWeekKey, loaded.Value.Version.Value, $"{loaded.Value.Aggregate.Id.Value:D}:{loaded.Value.Version.Value}", ct);
            }

            return loaded;
        }

        var scanned = await ExecuteReadAsync<LoadedAggregate<WeeklyDutyPlan>?>(async (connection, transaction) =>
        {
            var rows = await connection.QueryAsync<SnapshotScanRow>(
                """
                SELECT stream_id AS StreamId, last_included_version AS Version, snapshot_payload::text AS Payload
                FROM event_store_snapshots
                WHERE stream_type = @streamType;
                """,
                new { streamType = EventStoreDocuments.WeeklyDutyPlanStreamType },
                transaction: transaction);

            foreach (var row in rows)
            {
                var aggregate = EventStoreDocuments.DeserializeWeeklyDutyPlanSnapshot(row.StreamId, row.Payload);
                if (aggregate.AreaId == areaId && aggregate.WeekId.Equals(weekId))
                {
                    return new LoadedAggregate<WeeklyDutyPlan>(aggregate, new AggregateVersion(row.Version));
                }
            }

            return null;
        }, ct);

        if (scanned is not null)
        {
            await TryCachePlanAsync(scanned.Value.Aggregate, scanned.Value.Version.Value, ct);
            await TrySetPointerAsync(areaWeekKey, scanned.Value.Version.Value, $"{scanned.Value.Aggregate.Id.Value:D}:{scanned.Value.Version.Value}", ct);
        }

        return scanned;
    }

    public Task AddAsync(WeeklyDutyPlan aggregate, CancellationToken ct)
        => ExecuteWriteAsync(async (connection, transaction) =>
        {
            try
            {
                var streamId = aggregate.Id.Value;
                var currentVersion = await GetCurrentVersionAsync(connection, transaction, streamId, EventStoreDocuments.WeeklyDutyPlanStreamType);
                if (currentVersion > 0)
                {
                    throw new RepositoryDuplicateException($"WeeklyDutyPlan stream already exists: {aggregate.Id}");
                }

                var domainEvents = aggregate.DomainEvents.ToArray();
                await AppendEventsAsync(connection, transaction, streamId, EventStoreDocuments.WeeklyDutyPlanStreamType, 0, domainEvents);

                var targetVersion = Math.Max(1, domainEvents.Length);
                await UpsertSnapshotAsync(
                    connection,
                    transaction,
                    streamId,
                    EventStoreDocuments.WeeklyDutyPlanStreamType,
                    targetVersion,
                    EventStoreDocuments.SerializeSnapshot(aggregate));

                await TryCachePlanAsync(aggregate, targetVersion, ct);
            }
            catch (Exception ex)
            {
                throw PostgresExceptionMapper.Map(ex);
            }
        }, ct);

    public Task SaveAsync(WeeklyDutyPlan aggregate, AggregateVersion expectedVersion, CancellationToken ct)
        => ExecuteWriteAsync(async (connection, transaction) =>
        {
            try
            {
                var streamId = aggregate.Id.Value;
                var currentVersion = await GetCurrentVersionAsync(connection, transaction, streamId, EventStoreDocuments.WeeklyDutyPlanStreamType);
                if (currentVersion != expectedVersion.Value)
                {
                    throw new RepositoryConcurrencyException(
                        $"Expected version {expectedVersion.Value} but found {currentVersion} for stream {aggregate.Id}.");
                }

                var domainEvents = aggregate.DomainEvents.ToArray();
                await AppendEventsAsync(connection, transaction, streamId, EventStoreDocuments.WeeklyDutyPlanStreamType, expectedVersion.Value, domainEvents);

                var targetVersion = expectedVersion.Value + domainEvents.Length;
                await UpsertSnapshotAsync(
                    connection,
                    transaction,
                    streamId,
                    EventStoreDocuments.WeeklyDutyPlanStreamType,
                    targetVersion,
                    EventStoreDocuments.SerializeSnapshot(aggregate));

                await TryCachePlanAsync(aggregate, targetVersion, ct);

                var oldKey = _cacheKeyFactory.WeeklyPlanVersion(aggregate.Id, expectedVersion.Value);
                await TryDeleteWithRecoveryAsync(oldKey, targetVersion, ct);
            }
            catch (Exception ex)
            {
                throw PostgresExceptionMapper.Map(ex);
            }
        }, ct);

    private async Task TryCachePlanAsync(WeeklyDutyPlan aggregate, long version, CancellationToken ct)
    {
        try
        {
            var snapshot = EventStoreDocuments.SerializeSnapshot(aggregate);
            await _cache.SetAsync(_cacheKeyFactory.WeeklyPlanVersion(aggregate.Id, version), version, snapshot, _defaultTtl, ct);
            await _cache.SetAsync(_cacheKeyFactory.WeeklyPlanLatest(aggregate.Id), version, version.ToString(), _defaultTtl, ct);
            await _cache.SetAsync(
                _cacheKeyFactory.WeeklyPlanAreaWeekLatest(aggregate.AreaId, aggregate.WeekId),
                version,
                $"{aggregate.Id.Value:D}:{version}",
                _defaultTtl,
                ct);
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
            await _cache.DeleteAsync(cacheKey, ct);
        }
        catch (Exception ex)
        {
            await _invalidationTasks.EnqueueAsync(cacheKey, reasonGlobalPosition, ex.Message, ct);
        }
    }

    private async Task TrySetPointerAsync(string key, long version, string payload, CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(key, version, payload, _defaultTtl, ct);
        }
        catch
        {
            // Best-effort pointer update.
        }
    }

    private sealed record SnapshotRow(long Version, string Payload);
    private sealed record SnapshotScanRow(Guid StreamId, long Version, string Payload);
}

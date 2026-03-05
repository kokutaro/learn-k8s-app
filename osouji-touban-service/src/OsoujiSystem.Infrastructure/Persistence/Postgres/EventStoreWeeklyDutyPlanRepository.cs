using Dapper;
using Npgsql;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class EventStoreWeeklyDutyPlanRepository : PostgresRepositoryBase, IWeeklyDutyPlanRepository
{
    public EventStoreWeeklyDutyPlanRepository(
        NpgsqlDataSource dataSource,
        ITransactionContextAccessor transactionContextAccessor)
        : base(dataSource, transactionContextAccessor)
    {
    }

    public Task<LoadedAggregate<WeeklyDutyPlan>?> FindByIdAsync(WeeklyDutyPlanId planId, CancellationToken ct)
        => ExecuteReadAsync<LoadedAggregate<WeeklyDutyPlan>?>(async (connection, transaction) =>
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

    public async Task<LoadedAggregate<WeeklyDutyPlan>?> FindByAreaAndWeekAsync(
        CleaningAreaId areaId,
        WeekId weekId,
        CancellationToken ct)
    {
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
            return await FindByIdAsync(new WeeklyDutyPlanId(projectedPlanId.Value), ct);
        }

        return await ExecuteReadAsync<LoadedAggregate<WeeklyDutyPlan>?>(async (connection, transaction) =>
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
            }
            catch (Exception ex)
            {
                throw PostgresExceptionMapper.Map(ex);
            }
        }, ct);

    private sealed record SnapshotRow(long Version, string Payload);
    private sealed record SnapshotScanRow(Guid StreamId, long Version, string Payload);
}

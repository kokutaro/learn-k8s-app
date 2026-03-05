using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using OsoujiSystem.Infrastructure.Observability;
using OsoujiSystem.Infrastructure.Options;
using OsoujiSystem.Infrastructure.Persistence.Postgres;

namespace OsoujiSystem.Infrastructure.Projection;

internal sealed class MainProjectionWorker(
    MainProjector projector,
    IOptions<InfrastructureOptions> options,
    ILogger<MainProjectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(options.Value.Projection.PollIntervalMs);
        using var timer = new PeriodicTimer(pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var activity = OsoujiTelemetry.ActivitySource.StartActivity("projection.run_batch");
                var processed = await projector.RunBatchAsync(stoppingToken);
                activity?.SetTag("projection.batch.count", processed);
                if (processed > 0)
                {
                    logger.LogDebug("Projected {Count} events.", processed);
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Main projector batch failed.");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}

internal sealed class MainProjector(
    NpgsqlDataSource dataSource,
    IOptions<InfrastructureOptions> options)
{
    private const string ProjectorName = "main_projector";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<int> RunBatchAsync(CancellationToken ct)
    {
        var batchSize = options.Value.Projection.BatchSize;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var checkpoint = await LoadCheckpointAsync(connection, transaction);
        var events = (await connection.QueryAsync<EventEnvelope>(
            """
            SELECT global_position AS GlobalPosition,
                   stream_id AS StreamId,
                   stream_type AS StreamType
            FROM event_store_events
            WHERE global_position > @lastGlobalPosition
            ORDER BY global_position ASC
            LIMIT @batchSize;
            """,
            new { lastGlobalPosition = checkpoint, batchSize },
            transaction: transaction)).ToArray();

        if (events.Length == 0)
        {
            await transaction.CommitAsync(ct);
            return 0;
        }

        foreach (var ev in events)
        {
            if (string.Equals(ev.StreamType, EventStoreDocuments.CleaningAreaStreamType, StringComparison.Ordinal))
            {
                await ProjectCleaningAreaAsync(connection, transaction, ev.StreamId);
            }
            else if (string.Equals(ev.StreamType, EventStoreDocuments.WeeklyDutyPlanStreamType, StringComparison.Ordinal))
            {
                await ProjectWeeklyPlanAsync(connection, transaction, ev.StreamId);
            }
        }

        var lastPosition = events[^1].GlobalPosition;
        await connection.ExecuteAsync(
            """
            UPDATE projection_checkpoints
            SET last_global_position = @lastGlobalPosition,
                updated_at = now()
            WHERE projector_name = @projectorName;
            """,
            new { projectorName = ProjectorName, lastGlobalPosition = lastPosition },
            transaction: transaction);

        await transaction.CommitAsync(ct);
        return events.Length;
    }

    private static async Task<long> LoadCheckpointAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await connection.ExecuteAsync(
            """
            INSERT INTO projection_checkpoints (projector_name, last_global_position, updated_at)
            VALUES (@projectorName, 0, now())
            ON CONFLICT (projector_name) DO NOTHING;
            """,
            new { projectorName = ProjectorName },
            transaction: transaction);

        return await connection.QuerySingleAsync<long>(
            """
            SELECT last_global_position
            FROM projection_checkpoints
            WHERE projector_name = @projectorName
            FOR UPDATE;
            """,
            new { projectorName = ProjectorName },
            transaction: transaction);
    }

    private static async Task ProjectCleaningAreaAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid streamId)
    {
        var snapshot = await connection.QuerySingleOrDefaultAsync<SnapshotRow>(
            """
            SELECT last_included_version AS Version,
                   snapshot_payload::text AS Payload
            FROM event_store_snapshots
            WHERE stream_id = @streamId
              AND stream_type = @streamType;
            """,
            new { streamId, streamType = EventStoreDocuments.CleaningAreaStreamType },
            transaction: transaction);

        if (snapshot is null)
        {
            return;
        }

        var area = EventStoreDocuments.DeserializeCleaningAreaSnapshot(streamId, snapshot.Payload);

        await connection.ExecuteAsync(
            """
            INSERT INTO projection_cleaning_areas (
                area_id,
                area_name,
                current_week_rule,
                pending_week_rule,
                rotation_cursor,
                aggregate_version,
                updated_at
            )
            VALUES (
                @areaId,
                @areaName,
                CAST(@currentWeekRule AS jsonb),
                CAST(@pendingWeekRule AS jsonb),
                @rotationCursor,
                @aggregateVersion,
                now()
            )
            ON CONFLICT (area_id)
            DO UPDATE SET
                area_name = EXCLUDED.area_name,
                current_week_rule = EXCLUDED.current_week_rule,
                pending_week_rule = EXCLUDED.pending_week_rule,
                rotation_cursor = EXCLUDED.rotation_cursor,
                aggregate_version = EXCLUDED.aggregate_version,
                updated_at = now()
            WHERE projection_cleaning_areas.aggregate_version <= EXCLUDED.aggregate_version;
            """,
            new
            {
                areaId = area.Id.Value,
                areaName = area.Name,
                currentWeekRule = JsonSerializer.Serialize(area.CurrentWeekRule, JsonOptions),
                pendingWeekRule = area.PendingWeekRule.HasValue
                    ? JsonSerializer.Serialize(area.PendingWeekRule.Value, JsonOptions)
                    : null,
                rotationCursor = area.RotationCursor.Value,
                aggregateVersion = snapshot.Version
            },
            transaction: transaction);

        var activeUserIds = area.Members.Select(x => x.UserId.Value).ToArray();
        await connection.ExecuteAsync(
            """
            UPDATE projection_area_members
            SET is_active = false,
                updated_at = now()
            WHERE area_id = @areaId
              AND is_active = true
              AND NOT (user_id = ANY(@activeUserIds));
            """,
            new { areaId = area.Id.Value, activeUserIds },
            transaction: transaction);

        foreach (var member in area.Members)
        {
            await connection.ExecuteAsync(
                """
                UPDATE projection_area_members
                SET is_active = false,
                    updated_at = now()
                WHERE user_id = @userId
                  AND area_id <> @areaId
                  AND is_active = true;
                """,
                new { userId = member.UserId.Value, areaId = area.Id.Value },
                transaction: transaction);

            await connection.ExecuteAsync(
                """
                INSERT INTO projection_area_members (
                    area_id,
                    user_id,
                    area_member_id,
                    employee_number,
                    is_active,
                    updated_at
                )
                VALUES (
                    @areaId,
                    @userId,
                    @areaMemberId,
                    @employeeNumber,
                    true,
                    now()
                )
                ON CONFLICT (area_id, user_id)
                DO UPDATE SET
                    area_member_id = EXCLUDED.area_member_id,
                    employee_number = EXCLUDED.employee_number,
                    is_active = true,
                    updated_at = now();
                """,
                new
                {
                    areaId = area.Id.Value,
                    userId = member.UserId.Value,
                    areaMemberId = member.Id.Value,
                    employeeNumber = member.EmployeeNumber.Value
                },
                transaction: transaction);
        }
    }

    private static async Task ProjectWeeklyPlanAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid streamId)
    {
        var snapshot = await connection.QuerySingleOrDefaultAsync<SnapshotRow>(
            """
            SELECT last_included_version AS Version,
                   snapshot_payload::text AS Payload
            FROM event_store_snapshots
            WHERE stream_id = @streamId
              AND stream_type = @streamType;
            """,
            new { streamId, streamType = EventStoreDocuments.WeeklyDutyPlanStreamType },
            transaction: transaction);

        if (snapshot is null)
        {
            return;
        }

        var plan = EventStoreDocuments.DeserializeWeeklyDutyPlanSnapshot(streamId, snapshot.Payload);

        await connection.ExecuteAsync(
            """
            INSERT INTO projection_weekly_plans (
                plan_id,
                area_id,
                week_year,
                week_number,
                revision,
                status,
                fairness_window_weeks,
                updated_at
            )
            VALUES (
                @planId,
                @areaId,
                @weekYear,
                @weekNumber,
                @revision,
                @status,
                @fairnessWindowWeeks,
                now()
            )
            ON CONFLICT (plan_id)
            DO UPDATE SET
                area_id = EXCLUDED.area_id,
                week_year = EXCLUDED.week_year,
                week_number = EXCLUDED.week_number,
                revision = EXCLUDED.revision,
                status = EXCLUDED.status,
                fairness_window_weeks = EXCLUDED.fairness_window_weeks,
                updated_at = now()
            WHERE projection_weekly_plans.revision <= EXCLUDED.revision;
            """,
            new
            {
                planId = plan.Id.Value,
                areaId = plan.AreaId.Value,
                weekYear = plan.WeekId.Year,
                weekNumber = plan.WeekId.WeekNumber,
                revision = plan.Revision.Value,
                status = (short)plan.Status,
                fairnessWindowWeeks = plan.AssignmentPolicy.FairnessWindowWeeks
            },
            transaction: transaction);

        await connection.ExecuteAsync(
            "DELETE FROM projection_weekly_plan_assignments WHERE plan_id = @planId;",
            new { planId = plan.Id.Value },
            transaction: transaction);

        foreach (var assignment in plan.Assignments)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO projection_weekly_plan_assignments (
                    plan_id,
                    revision,
                    spot_id,
                    user_id,
                    updated_at
                )
                VALUES (@planId, @revision, @spotId, @userId, now());
                """,
                new
                {
                    planId = plan.Id.Value,
                    revision = plan.Revision.Value,
                    spotId = assignment.SpotId.Value,
                    userId = assignment.UserId.Value
                },
                transaction: transaction);
        }

        await connection.ExecuteAsync(
            "DELETE FROM projection_weekly_plan_offduty WHERE plan_id = @planId;",
            new { planId = plan.Id.Value },
            transaction: transaction);

        foreach (var offDuty in plan.OffDutyEntries)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO projection_weekly_plan_offduty (
                    plan_id,
                    revision,
                    user_id,
                    updated_at
                )
                VALUES (@planId, @revision, @userId, now());
                """,
                new
                {
                    planId = plan.Id.Value,
                    revision = plan.Revision.Value,
                    userId = offDuty.UserId.Value
                },
                transaction: transaction);
        }

        await connection.ExecuteAsync(
            """
            DELETE FROM projection_user_weekly_workloads
            WHERE area_id = @areaId
              AND week_year = @weekYear
              AND week_number = @weekNumber;
            """,
            new
            {
                areaId = plan.AreaId.Value,
                weekYear = plan.WeekId.Year,
                weekNumber = plan.WeekId.WeekNumber
            },
            transaction: transaction);

        var assignedByUser = plan.Assignments
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Count());

        var offDutyUsers = plan.OffDutyEntries
            .Select(x => x.UserId)
            .ToHashSet();

        var users = assignedByUser.Keys.Union(offDutyUsers).ToArray();
        foreach (var userId in users)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO projection_user_weekly_workloads (
                    area_id,
                    user_id,
                    week_year,
                    week_number,
                    assigned_count,
                    off_duty_count,
                    source_plan_id,
                    source_revision,
                    updated_at
                )
                VALUES (
                    @areaId,
                    @userId,
                    @weekYear,
                    @weekNumber,
                    @assignedCount,
                    @offDutyCount,
                    @sourcePlanId,
                    @sourceRevision,
                    now()
                );
                """,
                new
                {
                    areaId = plan.AreaId.Value,
                    userId = userId.Value,
                    weekYear = plan.WeekId.Year,
                    weekNumber = plan.WeekId.WeekNumber,
                    assignedCount = assignedByUser.GetValueOrDefault(userId, 0),
                    offDutyCount = offDutyUsers.Contains(userId) ? 1 : 0,
                    sourcePlanId = plan.Id.Value,
                    sourceRevision = plan.Revision.Value
                },
                transaction: transaction);
        }
    }

    private sealed record EventEnvelope(long GlobalPosition, Guid StreamId, string StreamType);
    private sealed record SnapshotRow(long Version, string Payload);
}

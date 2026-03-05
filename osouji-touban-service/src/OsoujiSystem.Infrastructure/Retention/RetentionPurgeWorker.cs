using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using OsoujiSystem.Infrastructure.Options;
using System.Globalization;

namespace OsoujiSystem.Infrastructure.Retention;

internal sealed class RetentionPurgeWorker : BackgroundService
{
    private const string JobName = "retention_purge_worker";

    private readonly NpgsqlDataSource _dataSource;
    private readonly IOptions<InfrastructureOptions> _options;
    private readonly ILogger<RetentionPurgeWorker> _logger;

    public RetentionPurgeWorker(
        NpgsqlDataSource dataSource,
        IOptions<InfrastructureOptions> options,
        ILogger<RetentionPurgeWorker> logger)
    {
        _dataSource = dataSource;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var nextRunUtc = ComputeNextRunUtc(nowUtc, _options.Value.Retention.DailyRunJst);
            var delay = nextRunUtc - nowUtc;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }

            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention purge job failed.");
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var retention = _options.Value.Retention;

        var plans = new[]
        {
            new PurgePlan("event_store_events", "recorded_at", nowUtc.AddYears(-retention.EventStoreYears)),
            new PurgePlan("event_store_snapshots", "updated_at", nowUtc.AddYears(-retention.EventStoreYears)),
            new PurgePlan("outbox_messages", "published_at", nowUtc.AddDays(-retention.OutboxPublishedDays), "published_at IS NOT NULL"),
            new PurgePlan("outbox_messages", "created_at", nowUtc.AddDays(-retention.OutboxFailedDays), "published_at IS NULL AND last_error IS NOT NULL"),
            new PurgePlan("consumer_processed_events", "processed_at", nowUtc.AddDays(-retention.DlqDays)),
            new PurgePlan("cache_invalidation_tasks", "resolved_at", nowUtc.AddDays(-retention.LogDays), "resolved_at IS NOT NULL")
        };

        foreach (var plan in plans)
        {
            await ExecutePlanAsync(plan, ct);
        }
    }

    internal static DateTimeOffset ComputeNextRunUtc(DateTimeOffset nowUtc, string dailyRunJst)
    {
        var runTimeJst = ParseDailyRunTime(dailyRunJst);
        var tokyo = GetTokyoTimeZone();
        var nowJst = TimeZoneInfo.ConvertTime(nowUtc.UtcDateTime, TimeZoneInfo.Utc, tokyo);

        var candidate = new DateTimeOffset(
            nowJst.Year,
            nowJst.Month,
            nowJst.Day,
            runTimeJst.Hours,
            runTimeJst.Minutes,
            0,
            tokyo.GetUtcOffset(nowJst));

        if (candidate <= nowJst)
        {
            candidate = candidate.AddDays(1);
        }

        return candidate.ToUniversalTime();
    }

    internal static TimeSpan ParseDailyRunTime(string value)
    {
        if (TimeSpan.TryParseExact(
                value,
                ["hh\\:mm", "h\\:mm"],
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return parsed;
        }

        return new TimeSpan(3, 30, 0);
    }

    private async Task ExecutePlanAsync(PurgePlan plan, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        try
        {
            var where = $"{plan.DateColumn} < @cutoffUtc";
            if (!string.IsNullOrWhiteSpace(plan.Predicate))
            {
                where = $"{where} AND {plan.Predicate}";
            }

            var deleteSql = $"DELETE FROM {plan.TableName} WHERE {where};";
            var deletedRows = await connection.ExecuteAsync(deleteSql, new { cutoffUtc = plan.CutoffUtc });

            await InsertReportAsync(connection, plan, deletedRows, "succeeded", null);
        }
        catch (Exception ex)
        {
            try
            {
                await InsertReportAsync(connection, plan, 0, "failed", ex.Message);
            }
            catch (Exception reportEx)
            {
                _logger.LogError(reportEx, "Failed to write purge report for table {TableName}.", plan.TableName);
            }

            _logger.LogError(ex, "Retention purge failed for table {TableName}.", plan.TableName);
        }
    }

    private static Task InsertReportAsync(
        NpgsqlConnection connection,
        PurgePlan plan,
        long deletedRows,
        string status,
        string? error)
        => connection.ExecuteAsync(
            """
            INSERT INTO data_retention_purge_reports (
                report_id,
                job_name,
                target_table,
                purge_from,
                purge_to,
                deleted_rows,
                status,
                error_message,
                executed_at
            )
            VALUES (
                @reportId,
                @jobName,
                @targetTable,
                @purgeFrom,
                @purgeTo,
                @deletedRows,
                @status,
                @errorMessage,
                now()
            );
            """,
            new
            {
                reportId = Guid.NewGuid(),
                jobName = JobName,
                targetTable = plan.TableName,
                purgeFrom = DateTimeOffset.UnixEpoch,
                purgeTo = plan.CutoffUtc,
                deletedRows,
                status,
                errorMessage = error
            });

    private static TimeZoneInfo GetTokyoTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        }
    }

    private sealed record PurgePlan(
        string TableName,
        string DateColumn,
        DateTimeOffset CutoffUtc,
        string? Predicate = null);
}

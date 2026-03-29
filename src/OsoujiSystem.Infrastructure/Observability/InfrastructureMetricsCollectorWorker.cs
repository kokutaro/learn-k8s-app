using System.Diagnostics.Metrics;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace OsoujiSystem.Infrastructure.Observability;

internal sealed class InfrastructureMetricsCollectorWorker : BackgroundService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<InfrastructureMetricsCollectorWorker> _logger;
    private readonly Lock _sync = new();
    private Dictionary<string, decimal> _projectionLagByProjector = new(StringComparer.Ordinal);
    private Dictionary<string, long> _readModelVisibilityCheckpointGapByProjector = new(StringComparer.Ordinal);
    private Dictionary<string, long> _readModelCacheInvalidationTasksPendingByProjector = new(StringComparer.Ordinal);
    private long _outboxPendingCount;

    public InfrastructureMetricsCollectorWorker(
        NpgsqlDataSource dataSource,
        ILogger<InfrastructureMetricsCollectorWorker> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
        OsoujiTelemetry.SetProjectionLagProvider(ObserveProjectionLag);
        OsoujiTelemetry.SetOutboxPendingProvider(ObserveOutboxPending);
        OsoujiTelemetry.SetReadModelVisibilityCheckpointGapProvider(ObserveReadModelVisibilityCheckpointGap);
        OsoujiTelemetry.SetReadModelCacheInvalidationTasksPendingProvider(ObserveReadModelCacheInvalidationTasksPending);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Infrastructure metrics collection failed.");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task CollectOnceAsync(CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct);

        var projectorRows = (await connection.QueryAsync<ProjectorLagRow>(
            """
            SELECT projector_name AS ProjectorName,
                   GREATEST(EXTRACT(EPOCH FROM (now() - updated_at)), 0) AS LagSeconds
            FROM projection_checkpoints;
            """)).ToArray();

        var visibilityRows = (await connection.QueryAsync<ReadModelVisibilityMetricsRow>(
            """
            SELECT pc.projector_name AS ProjectorName,
                   GREATEST(pc.last_global_position - COALESCE(rvc.last_visible_global_position, 0), 0) AS VisibilityCheckpointGap,
                   (
                       SELECT COUNT(1)
                       FROM readmodel_cache_invalidation_tasks tasks
                       WHERE tasks.projector_name = pc.projector_name
                         AND tasks.resolved_at IS NULL
                   ) AS PendingInvalidationTasks
            FROM projection_checkpoints pc
            LEFT JOIN readmodel_visibility_checkpoints rvc
                ON rvc.projector_name = pc.projector_name;
            """)).ToArray();

        var outboxPending = await connection.ExecuteScalarAsync<long>(
            """
            SELECT COUNT(1)
            FROM outbox_messages
            WHERE published_at IS NULL;
            """);

        var latest = projectorRows.ToDictionary(x => x.ProjectorName, x => x.LagSeconds, StringComparer.Ordinal);
        var visibilityGap = visibilityRows.ToDictionary(x => x.ProjectorName, x => x.VisibilityCheckpointGap, StringComparer.Ordinal);
        var pendingInvalidationTasks = visibilityRows.ToDictionary(x => x.ProjectorName, x => x.PendingInvalidationTasks, StringComparer.Ordinal);
        lock (_sync)
        {
            _projectionLagByProjector = latest;
            _readModelVisibilityCheckpointGapByProjector = visibilityGap;
            _readModelCacheInvalidationTasksPendingByProjector = pendingInvalidationTasks;
            _outboxPendingCount = outboxPending;
        }
    }

    private IEnumerable<Measurement<double>> ObserveProjectionLag()
    {
        lock (_sync)
        {
            return _projectionLagByProjector
                .Select(x => new Measurement<double>((double)x.Value, new KeyValuePair<string, object?>("projector", x.Key)))
                .ToArray();
        }
    }

    private IEnumerable<Measurement<long>> ObserveOutboxPending()
    {
        lock (_sync)
        {
            return [new Measurement<long>(_outboxPendingCount)];
        }
    }

    private IEnumerable<Measurement<long>> ObserveReadModelVisibilityCheckpointGap()
    {
        lock (_sync)
        {
            return _readModelVisibilityCheckpointGapByProjector
                .Select(x => new Measurement<long>(x.Value, new KeyValuePair<string, object?>("projector", x.Key)))
                .ToArray();
        }
    }

    private IEnumerable<Measurement<long>> ObserveReadModelCacheInvalidationTasksPending()
    {
        lock (_sync)
        {
            return _readModelCacheInvalidationTasksPendingByProjector
                .Select(x => new Measurement<long>(x.Value, new KeyValuePair<string, object?>("projector", x.Key)))
                .ToArray();
        }
    }

    private sealed record ProjectorLagRow(string ProjectorName, decimal LagSeconds);
    private sealed record ReadModelVisibilityMetricsRow(
        string ProjectorName,
        long VisibilityCheckpointGap,
        long PendingInvalidationTasks);
}

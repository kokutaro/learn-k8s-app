using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OsoujiSystem.Infrastructure.Cache;
using OsoujiSystem.Infrastructure.Observability;
using OsoujiSystem.Infrastructure.Options;
using OsoujiSystem.Infrastructure.Queries.Caching;

namespace OsoujiSystem.Infrastructure.Projection;

internal sealed class ReadModelCacheInvalidationRecoveryWorker(
    IReadModelCacheInvalidationTaskRepository taskRepository,
    IReadModelCache readModelCache,
    IReadModelVisibilityCheckpointAdvancer checkpointAdvancer,
    IOptions<InfrastructureOptions> options,
    ILogger<ReadModelCacheInvalidationRecoveryWorker> logger) : BackgroundService
{
    private readonly TimeSpan _readModelDetailTtl = TimeSpan.FromSeconds(options.Value.Redis.ReadModelDetailTtlSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dueTasks = await taskRepository.ListDueAsync(100, DateTimeOffset.UtcNow, stoppingToken);
                foreach (var task in dueTasks)
                {
                    await ProcessAsync(task, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ReadModel cache invalidation recovery batch failed.");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    internal async Task ProcessAsync(ReadModelCacheInvalidationTask task, CancellationToken ct)
    {
        try
        {
            await ExecuteOperationAsync(task, ct);
            await taskRepository.MarkResolvedAsync(task.TaskId, ct);
            await checkpointAdvancer.AdvanceAsync(task.ProjectorName, ct);
        }
        catch (Exception ex)
        {
            var nextRetryCount = task.RetryCount + 1;
            var nextRetryAt = DateTimeOffset.UtcNow.Add(CacheInvalidationRecoveryWorker.ComputeBackoff(nextRetryCount));
            await taskRepository.MarkFailedAsync(task.TaskId, nextRetryCount, nextRetryAt, ex.Message, ct);

            OsoujiTelemetry.ReadModelCacheRefreshFailuresTotal.Add(
                1,
                new KeyValuePair<string, object?>("resource", DetectResource(task.CacheKey)),
                new KeyValuePair<string, object?>("scope", "recovery_worker"));

            logger.LogWarning(ex, "ReadModel cache invalidation retry failed for key {CacheKey}", task.CacheKey);
        }
    }

    private Task ExecuteOperationAsync(ReadModelCacheInvalidationTask task, CancellationToken ct)
        => task.OperationKind switch
        {
            ReadModelCacheInvalidationOperationKind.Remove => readModelCache.RemoveAsync(task.CacheKey, ct),
            ReadModelCacheInvalidationOperationKind.IncrementNamespace => readModelCache.IncrementNamespaceVersionAsync(task.CacheKey, _readModelDetailTtl, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(task.OperationKind), task.OperationKind, null)
        };

    internal static string DetectResource(string cacheKey)
        => cacheKey switch
        {
            var key when key.Contains("facility", StringComparison.Ordinal) => "facility",
            var key when key.Contains("cleaning-area", StringComparison.Ordinal) => "cleaning_area",
            var key when key.Contains("weekly-plan", StringComparison.Ordinal) => "weekly_plan",
            _ => "unknown"
        };
}

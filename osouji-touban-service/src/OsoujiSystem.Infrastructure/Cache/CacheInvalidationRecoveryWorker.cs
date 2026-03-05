using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OsoujiSystem.Infrastructure.Cache;

internal sealed class CacheInvalidationRecoveryWorker : BackgroundService
{
    private readonly ICacheInvalidationTaskRepository _taskRepository;
    private readonly IAggregateCache _aggregateCache;
    private readonly ILogger<CacheInvalidationRecoveryWorker> _logger;

    public CacheInvalidationRecoveryWorker(
        ICacheInvalidationTaskRepository taskRepository,
        IAggregateCache aggregateCache,
        ILogger<CacheInvalidationRecoveryWorker> logger)
    {
        _taskRepository = taskRepository;
        _aggregateCache = aggregateCache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dueTasks = await _taskRepository.ListDueAsync(100, DateTimeOffset.UtcNow, stoppingToken);
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
                _logger.LogError(ex, "Cache invalidation recovery batch failed.");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    internal static TimeSpan ComputeBackoff(int retryCount)
        => retryCount switch
        {
            <= 1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            4 => TimeSpan.FromHours(1),
            _ => TimeSpan.FromHours(6)
        };

    private async Task ProcessAsync(CacheInvalidationTask task, CancellationToken ct)
    {
        try
        {
            await _aggregateCache.DeleteAsync(task.CacheKey, ct);
            await _taskRepository.MarkResolvedAsync(task.TaskId, ct);
        }
        catch (Exception ex)
        {
            var nextRetryCount = task.RetryCount + 1;
            var nextRetryAt = DateTimeOffset.UtcNow.Add(ComputeBackoff(nextRetryCount));
            await _taskRepository.MarkFailedAsync(task.TaskId, nextRetryCount, nextRetryAt, ex.Message, ct);
            _logger.LogWarning(ex, "Cache invalidation retry failed for key {CacheKey}", task.CacheKey);
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OsoujiSystem.Application.UseCases.WeeklyDutyPlans;
using OsoujiSystem.Infrastructure.Options;

namespace OsoujiSystem.Infrastructure.Scheduling;

internal sealed class AutoDutyPlanSchedulerWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<InfrastructureOptions> options,
    ILogger<AutoDutyPlanSchedulerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedulerOptions = options.Value.AutoScheduler;
        if (!schedulerOptions.Enabled)
        {
            logger.LogInformation("Auto duty plan scheduler is disabled.");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(schedulerOptions.PollIntervalSeconds);
        logger.LogInformation(
            "Auto duty plan scheduler started with poll interval {PollInterval}s.",
            schedulerOptions.PollIntervalSeconds);

        using var timer = new PeriodicTimer(pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
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
                logger.LogError(ex, "Auto duty plan scheduler iteration failed.");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AutoSchedulePlanService>();
        await service.ProcessAllAreasAsync(ct);
    }
}

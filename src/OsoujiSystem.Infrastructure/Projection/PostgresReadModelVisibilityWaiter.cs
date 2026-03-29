using Microsoft.Extensions.Options;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Infrastructure.Options;

namespace OsoujiSystem.Infrastructure.Projection;

internal sealed class PostgresReadModelVisibilityWaiter(
    IReadModelVisibilityCheckpointRepository checkpointRepository,
    IOptions<InfrastructureOptions> options) : IReadModelVisibilityWaiter
{
    public async Task<ReadModelVisibilityWaitResult> WaitUntilVisibleAsync(
        ReadModelConsistencyToken token,
        CancellationToken ct)
    {
        var timeout = TimeSpan.FromMilliseconds(options.Value.ProjectionVisibility.WaitTimeoutMs);
        var pollInterval = TimeSpan.FromMilliseconds(options.Value.ProjectionVisibility.PollIntervalMs);
        var startedAt = TimeProvider.System.GetTimestamp();

        while (true)
        {
            var state = await checkpointRepository.GetStateAsync(MainProjector.ProjectorName, ct);
            var waited = TimeProvider.System.GetElapsedTime(startedAt);
            if (state.VisibilityCheckpoint >= token.RequiredGlobalPosition)
            {
                return new ReadModelVisibilityWaitResult(
                    IsVisible: true,
                    TimedOut: false,
                    Waited: waited);
            }

            var remaining = timeout - waited;
            if (remaining <= TimeSpan.Zero)
            {
                return new ReadModelVisibilityWaitResult(
                    IsVisible: false,
                    TimedOut: true,
                    Waited: waited);
            }

            await Task.Delay(remaining < pollInterval ? remaining : pollInterval, ct);
        }
    }
}

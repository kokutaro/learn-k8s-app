using AwesomeAssertions;
using Microsoft.Extensions.Options;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Infrastructure.Options;
using OsoujiSystem.Infrastructure.Projection;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class PostgresReadModelVisibilityWaiterTests
{
    [Fact]
    public async Task WaitUntilVisibleAsync_ShouldReturnImmediately_WhenCheckpointAlreadyVisible()
    {
        var repository = new SequenceRepository(
            new ReadModelVisibilityCheckpointState("main_projector", 10, 12, null));
        var waiter = new PostgresReadModelVisibilityWaiter(repository, CreateOptions(waitTimeoutMs: 100, pollIntervalMs: 10));

        var result = await waiter.WaitUntilVisibleAsync(new ReadModelConsistencyToken(11), TestContext.Current.CancellationToken);

        result.IsVisible.Should().BeTrue();
        result.TimedOut.Should().BeFalse();
        repository.ReadCount.Should().Be(1);
    }

    [Fact]
    public async Task WaitUntilVisibleAsync_ShouldPollUntilCheckpointBecomesVisible()
    {
        var repository = new SequenceRepository(
            new ReadModelVisibilityCheckpointState("main_projector", 10, 5, null),
            new ReadModelVisibilityCheckpointState("main_projector", 10, 8, null),
            new ReadModelVisibilityCheckpointState("main_projector", 10, 10, null));
        var waiter = new PostgresReadModelVisibilityWaiter(repository, CreateOptions(waitTimeoutMs: 200, pollIntervalMs: 1));

        var result = await waiter.WaitUntilVisibleAsync(new ReadModelConsistencyToken(10), TestContext.Current.CancellationToken);

        result.IsVisible.Should().BeTrue();
        result.TimedOut.Should().BeFalse();
        repository.ReadCount.Should().Be(3);
    }

    [Fact]
    public async Task WaitUntilVisibleAsync_ShouldReturnTimedOut_WhenCheckpointDoesNotAdvanceInTime()
    {
        var repository = new SequenceRepository(
            new ReadModelVisibilityCheckpointState("main_projector", 10, 5, null));
        var waiter = new PostgresReadModelVisibilityWaiter(repository, CreateOptions(waitTimeoutMs: 20, pollIntervalMs: 5));

        var result = await waiter.WaitUntilVisibleAsync(new ReadModelConsistencyToken(10), TestContext.Current.CancellationToken);

        result.IsVisible.Should().BeFalse();
        result.TimedOut.Should().BeTrue();
        result.Waited.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        repository.ReadCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task WaitUntilVisibleAsync_ShouldHonorCancellationDuringPolling()
    {
        var repository = new SequenceRepository(
            new ReadModelVisibilityCheckpointState("main_projector", 10, 5, null));
        var waiter = new PostgresReadModelVisibilityWaiter(repository, CreateOptions(waitTimeoutMs: 500, pollIntervalMs: 100));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        var action = async () => await waiter.WaitUntilVisibleAsync(new ReadModelConsistencyToken(10), cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static IOptions<InfrastructureOptions> CreateOptions(int waitTimeoutMs, int pollIntervalMs)
        => Microsoft.Extensions.Options.Options.Create(new InfrastructureOptions
        {
            ProjectionVisibility = new ProjectionVisibilityOptions
            {
                Enabled = true,
                WaitTimeoutMs = waitTimeoutMs,
                PollIntervalMs = pollIntervalMs
            }
        });

    private sealed class SequenceRepository(params ReadModelVisibilityCheckpointState[] states)
        : IReadModelVisibilityCheckpointRepository
    {
        private readonly IReadOnlyList<ReadModelVisibilityCheckpointState> _states = states;
        private int _index;

        public int ReadCount { get; private set; }

        public Task<ReadModelVisibilityCheckpointState> GetStateAsync(string projectorName, CancellationToken ct)
        {
            ReadCount++;
            var state = _states[Math.Min(_index, _states.Count - 1)];
            if (_index < _states.Count - 1)
            {
                _index++;
            }

            return Task.FromResult(state with { ProjectorName = projectorName });
        }

        public Task UpsertVisibilityCheckpointAsync(string projectorName, long lastVisibleGlobalPosition, CancellationToken ct)
            => Task.CompletedTask;
    }
}

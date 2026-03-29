using AwesomeAssertions;
using OsoujiSystem.Infrastructure.Projection;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class ReadModelVisibilityCheckpointAdvancerTests
{
    public static TheoryData<long, long?, long> CalculateVisiblePositionCases => new()
    {
        { 10, null, 10 },
        { 10, 15, 10 },
        { 10, 10, 9 },
        { 10, 4, 3 },
        { 0, 1, 0 }
    };

    [Theory]
    [MemberData(nameof(CalculateVisiblePositionCases))]
    public void CalculateVisiblePosition_ShouldFollowVisibilityBarrier(
        long projectionCheckpoint,
        long? minPendingInvalidationPosition,
        long expected)
    {
        ReadModelVisibilityCheckpointAdvancer.CalculateVisiblePosition(
                projectionCheckpoint,
                minPendingInvalidationPosition)
            .Should().Be(expected);
    }

    [Fact]
    public async Task AdvanceAsync_ShouldPersistCalculatedVisibilityCheckpoint()
    {
        var repository = new FakeRepository(
            new ReadModelVisibilityCheckpointState(
                "main_projector",
                12,
                5,
                9));
        var advancer = new ReadModelVisibilityCheckpointAdvancer(repository);

        var result = await advancer.AdvanceAsync("main_projector", TestContext.Current.CancellationToken);

        result.Advanced.Should().BeTrue();
        result.NewVisibilityCheckpoint.Should().Be(8);
        repository.UpsertedProjectorName.Should().Be("main_projector");
        repository.UpsertedVisibilityCheckpoint.Should().Be(8);
    }

    [Fact]
    public async Task AdvanceAsync_ShouldSkipWrite_WhenCheckpointIsUnchanged()
    {
        var repository = new FakeRepository(
            new ReadModelVisibilityCheckpointState(
                "main_projector",
                12,
                12,
                null));
        var advancer = new ReadModelVisibilityCheckpointAdvancer(repository);

        var result = await advancer.AdvanceAsync("main_projector", TestContext.Current.CancellationToken);

        result.Advanced.Should().BeFalse();
        repository.UpsertedProjectorName.Should().BeNull();
    }

    private sealed class FakeRepository(ReadModelVisibilityCheckpointState state) : IReadModelVisibilityCheckpointRepository
    {
        public string? UpsertedProjectorName { get; private set; }
        public long? UpsertedVisibilityCheckpoint { get; private set; }

        public Task<ReadModelVisibilityCheckpointState> GetStateAsync(string projectorName, CancellationToken ct)
            => Task.FromResult(state with { ProjectorName = projectorName });

        public Task UpsertVisibilityCheckpointAsync(string projectorName, long lastVisibleGlobalPosition, CancellationToken ct)
        {
            UpsertedProjectorName = projectorName;
            UpsertedVisibilityCheckpoint = lastVisibleGlobalPosition;
            return Task.CompletedTask;
        }
    }
}

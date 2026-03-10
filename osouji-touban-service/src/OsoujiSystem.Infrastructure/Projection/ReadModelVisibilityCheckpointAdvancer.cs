namespace OsoujiSystem.Infrastructure.Projection;

internal sealed class ReadModelVisibilityCheckpointAdvancer(
    IReadModelVisibilityCheckpointRepository repository) : IReadModelVisibilityCheckpointAdvancer
{
    public async Task<ReadModelVisibilityAdvanceResult> AdvanceAsync(string projectorName, CancellationToken ct)
    {
        var state = await repository.GetStateAsync(projectorName, ct);
        var newVisibilityCheckpoint = CalculateVisiblePosition(
            state.ProjectionCheckpoint,
            state.MinPendingInvalidationPosition);

        if (newVisibilityCheckpoint != state.VisibilityCheckpoint)
        {
            await repository.UpsertVisibilityCheckpointAsync(projectorName, newVisibilityCheckpoint, ct);
        }

        return new ReadModelVisibilityAdvanceResult(
            projectorName,
            state.ProjectionCheckpoint,
            state.VisibilityCheckpoint,
            newVisibilityCheckpoint,
            state.MinPendingInvalidationPosition,
            newVisibilityCheckpoint != state.VisibilityCheckpoint);
    }

    internal static long CalculateVisiblePosition(long projectionCheckpoint, long? minPendingInvalidationPosition)
    {
        if (minPendingInvalidationPosition is null)
        {
            return projectionCheckpoint;
        }

        if (minPendingInvalidationPosition <= 0)
        {
            return 0;
        }

        return Math.Min(projectionCheckpoint, minPendingInvalidationPosition.Value - 1);
    }
}

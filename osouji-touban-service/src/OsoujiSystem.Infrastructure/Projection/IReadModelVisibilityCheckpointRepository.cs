namespace OsoujiSystem.Infrastructure.Projection;

internal sealed record ReadModelVisibilityCheckpointState(
    string ProjectorName,
    long ProjectionCheckpoint,
    long VisibilityCheckpoint,
    long? MinPendingInvalidationPosition);

internal interface IReadModelVisibilityCheckpointRepository
{
    Task<ReadModelVisibilityCheckpointState> GetStateAsync(string projectorName, CancellationToken ct);
    Task UpsertVisibilityCheckpointAsync(string projectorName, long lastVisibleGlobalPosition, CancellationToken ct);
}

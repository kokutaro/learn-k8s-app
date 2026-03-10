namespace OsoujiSystem.Infrastructure.Projection;

internal sealed record ReadModelVisibilityAdvanceResult(
    string ProjectorName,
    long ProjectionCheckpoint,
    long PreviousVisibilityCheckpoint,
    long NewVisibilityCheckpoint,
    long? MinPendingInvalidationPosition,
    bool Advanced);

internal interface IReadModelVisibilityCheckpointAdvancer
{
    Task<ReadModelVisibilityAdvanceResult> AdvanceAsync(string projectorName, CancellationToken ct);
}

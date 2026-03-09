namespace OsoujiSystem.Application.Abstractions;

public readonly record struct ReadModelConsistencyToken(long RequiredGlobalPosition);

public readonly record struct ReadModelVisibilityWaitResult(
    bool IsVisible,
    bool TimedOut,
    TimeSpan Waited);

public interface IReadModelConsistencyContextAccessor
{
    bool TryGet(out ReadModelConsistencyToken token);
    void Set(ReadModelConsistencyToken token);
    void Clear();
}

public interface IReadModelVisibilityWaiter
{
    Task<ReadModelVisibilityWaitResult> WaitUntilVisibleAsync(
        ReadModelConsistencyToken token,
        CancellationToken ct);
}

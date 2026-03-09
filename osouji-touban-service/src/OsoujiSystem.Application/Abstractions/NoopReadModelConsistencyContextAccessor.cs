namespace OsoujiSystem.Application.Abstractions;

public sealed class NoopReadModelConsistencyContextAccessor : IReadModelConsistencyContextAccessor
{
    public bool TryGet(out ReadModelConsistencyToken token)
    {
        token = default;
        return false;
    }

    public void Set(ReadModelConsistencyToken token)
    {
    }

    public void Clear()
    {
    }
}

public sealed class NoopReadModelVisibilityWaiter : IReadModelVisibilityWaiter
{
    public Task<ReadModelVisibilityWaitResult> WaitUntilVisibleAsync(
        ReadModelConsistencyToken token,
        CancellationToken ct)
        => Task.FromResult(new ReadModelVisibilityWaitResult(
            IsVisible: true,
            TimedOut: false,
            Waited: TimeSpan.Zero));
}

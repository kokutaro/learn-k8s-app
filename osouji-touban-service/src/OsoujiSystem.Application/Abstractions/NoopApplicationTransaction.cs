namespace OsoujiSystem.Application.Abstractions;

public sealed class NoopApplicationTransaction : IApplicationTransaction
{
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        return action(ct);
    }
}

namespace OsoujiSystem.Application.Abstractions;

public interface IApplicationTransaction
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct);
}

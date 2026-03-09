using Npgsql;
using OsoujiSystem.Application.Abstractions;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class NpgsqlApplicationTransaction(
    NpgsqlDataSource dataSource,
    ITransactionContextAccessor contextAccessor,
    IEventWriteContextAccessor eventWriteContextAccessor,
    IReadModelConsistencyContextAccessor readModelConsistencyContextAccessor) : IApplicationTransaction
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        if (contextAccessor.HasActiveTransaction)
        {
            return await action(ct);
        }

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        contextAccessor.Set(connection, transaction);
        eventWriteContextAccessor.Initialize();
        readModelConsistencyContextAccessor.Clear();

        try
        {
            var result = await action(ct);
            await transaction.CommitAsync(ct);
            PublishConsistencyTokenIfAvailable(eventWriteContextAccessor, readModelConsistencyContextAccessor);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            readModelConsistencyContextAccessor.Clear();
            throw;
        }
        finally
        {
            contextAccessor.Clear();
            eventWriteContextAccessor.Clear();
        }
    }

    internal static void PublishConsistencyTokenIfAvailable(
        IEventWriteContextAccessor eventWriteContextAccessor,
        IReadModelConsistencyContextAccessor readModelConsistencyContextAccessor)
    {
        if (eventWriteContextAccessor.TryGetMaxGlobalPosition(out var globalPosition))
        {
            readModelConsistencyContextAccessor.Set(new ReadModelConsistencyToken(globalPosition));
        }
    }
}

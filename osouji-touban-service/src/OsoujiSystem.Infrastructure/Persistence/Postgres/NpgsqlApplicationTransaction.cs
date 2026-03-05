using Npgsql;
using OsoujiSystem.Application.Abstractions;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class NpgsqlApplicationTransaction : IApplicationTransaction
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ITransactionContextAccessor _contextAccessor;
    private readonly IEventWriteContextAccessor _eventWriteContextAccessor;

    public NpgsqlApplicationTransaction(
        NpgsqlDataSource dataSource,
        ITransactionContextAccessor contextAccessor,
        IEventWriteContextAccessor eventWriteContextAccessor)
    {
        _dataSource = dataSource;
        _contextAccessor = contextAccessor;
        _eventWriteContextAccessor = eventWriteContextAccessor;
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        if (_contextAccessor.HasActiveTransaction)
        {
            return await action(ct);
        }

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        _contextAccessor.Set(connection, transaction);

        try
        {
            var result = await action(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            _contextAccessor.Clear();
            _eventWriteContextAccessor.Clear();
        }
    }
}

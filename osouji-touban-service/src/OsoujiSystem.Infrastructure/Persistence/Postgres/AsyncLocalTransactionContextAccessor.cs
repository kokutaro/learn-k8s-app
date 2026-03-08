using Npgsql;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class AsyncLocalTransactionContextAccessor : ITransactionContextAccessor
{
    private sealed class TransactionContext
    {
        public required NpgsqlConnection Connection { get; init; }
        public required NpgsqlTransaction Transaction { get; init; }
    }

    private readonly AsyncLocal<TransactionContext?> _context = new();

    public NpgsqlConnection? Connection => _context.Value?.Connection;
    public NpgsqlTransaction? Transaction => _context.Value?.Transaction;
    public bool HasActiveTransaction => _context.Value is not null;

    public void Set(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _context.Value = new TransactionContext
        {
            Connection = connection,
            Transaction = transaction
        };
    }

    public void Clear() => _context.Value = null;
}

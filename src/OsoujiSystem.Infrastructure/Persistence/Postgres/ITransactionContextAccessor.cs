using Npgsql;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal interface ITransactionContextAccessor
{
    NpgsqlConnection? Connection { get; }
    NpgsqlTransaction? Transaction { get; }
    bool HasActiveTransaction { get; }
    void Set(NpgsqlConnection connection, NpgsqlTransaction transaction);
    void Clear();
}

using Npgsql;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal static class PostgresExceptionMapper
{
    private const string UniqueViolation = "23505";

    public static Exception Map(Exception exception)
    {
        if (exception is not PostgresException postgresException)
        {
            return exception;
        }

        if (postgresException.SqlState != UniqueViolation)
        {
            return exception;
        }

        if (string.Equals(postgresException.ConstraintName, "uq_event_stream_version", StringComparison.OrdinalIgnoreCase))
        {
            return new RepositoryConcurrencyException(postgresException.MessageText);
        }

        return new RepositoryDuplicateException(postgresException.MessageText);
    }
}

using Dapper;
using Npgsql;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.UserManagement;
using OsoujiSystem.Domain.ValueObjects;
using OsoujiSystem.Infrastructure.Serialization;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class PostgresUserDirectoryProjectionRepository(
    NpgsqlDataSource dataSource,
    ITransactionContextAccessor transactionContextAccessor,
    IEventWriteContextAccessor eventWriteContextAccessor,
    EventStoreDocuments eventStoreDocuments,
    InfrastructureJsonSerializer jsonSerializer)
    : PostgresRepositoryBase(dataSource, transactionContextAccessor, eventWriteContextAccessor, eventStoreDocuments, jsonSerializer), IUserDirectoryProjectionRepository
{
    public Task<UserDirectoryProjection?> FindByUserIdAsync(UserId userId, CancellationToken ct)
        => ExecuteReadAsync<UserDirectoryProjection?>(async (connection, transaction) =>
        {
            var row = await connection.QuerySingleOrDefaultAsync<UserDirectoryProjectionRow>(
                """
                SELECT user_id AS UserId,
                       employee_number AS EmployeeNumber,
                       display_name AS DisplayName,
                       email_address AS EmailAddress,
                       lifecycle_status AS LifecycleStatus,
                       department_code AS DepartmentCode,
                       aggregate_version AS AggregateVersion
                FROM projection_user_directory
                WHERE user_id = @userId;
                """,
                new { userId = userId.Value },
                transaction: transaction);

            return row is null
                ? null
                : new UserDirectoryProjection(
                    new UserId(row.UserId),
                    EmployeeNumber.Create(row.EmployeeNumber).Value,
                    row.DisplayName,
                    Enum.Parse<ManagedUserLifecycleStatus>(row.LifecycleStatus, ignoreCase: true),
                    row.DepartmentCode,
                    row.AggregateVersion,
                    row.EmailAddress);
        }, ct);

    public Task UpsertAsync(
        UserDirectoryProjection projection,
        long aggregateVersion,
        Guid sourceEventId,
        CancellationToken ct)
        => ExecuteWriteAsync(async (connection, transaction) =>
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO projection_user_directory (
                    user_id,
                    employee_number,
                    display_name,
                    email_address,
                    lifecycle_status,
                    department_code,
                    source_event_id,
                    aggregate_version,
                    updated_at
                )
                VALUES (
                    @userId,
                    @employeeNumber,
                    @displayName,
                    @emailAddress,
                    @lifecycleStatus,
                    @departmentCode,
                    @sourceEventId,
                    @aggregateVersion,
                    now()
                )
                ON CONFLICT (user_id)
                DO UPDATE SET
                    employee_number = EXCLUDED.employee_number,
                    display_name = EXCLUDED.display_name,
                    email_address = EXCLUDED.email_address,
                    lifecycle_status = EXCLUDED.lifecycle_status,
                    department_code = EXCLUDED.department_code,
                    source_event_id = EXCLUDED.source_event_id,
                    aggregate_version = EXCLUDED.aggregate_version,
                    updated_at = now()
                WHERE projection_user_directory.aggregate_version <= EXCLUDED.aggregate_version;
                """,
                new
                {
                    userId = projection.UserId.Value,
                    employeeNumber = projection.EmployeeNumber.Value,
                    displayName = projection.DisplayName,
                    emailAddress = projection.EmailAddress,
                    lifecycleStatus = projection.LifecycleStatus.ToString(),
                    departmentCode = projection.DepartmentCode,
                    sourceEventId,
                    aggregateVersion
                },
                transaction: transaction);
        }, ct);

    private sealed record UserDirectoryProjectionRow(
        Guid UserId,
        string EmployeeNumber,
        string DisplayName,
        string? EmailAddress,
        string LifecycleStatus,
        string? DepartmentCode,
        long AggregateVersion);
}

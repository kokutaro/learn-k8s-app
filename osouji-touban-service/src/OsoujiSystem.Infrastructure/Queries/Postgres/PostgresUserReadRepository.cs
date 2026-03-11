using Dapper;
using Npgsql;
using OsoujiSystem.Application.Queries.Abstractions;
using OsoujiSystem.Application.Queries.Shared;
using OsoujiSystem.Application.Queries.Users;

namespace OsoujiSystem.Infrastructure.Queries.Postgres;

internal sealed class PostgresUserReadRepository(
    NpgsqlDataSource dataSource,
    PostgresReadModelHelpers readModelHelpers) : IUserReadRepository
{
    public async Task<UserDetailReadModel?> FindByIdAsync(
        Guid userId,
        CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<DetailRow>(
            """
            SELECT
                u.user_id AS Id,
                u.employee_number AS EmployeeNumber,
                u.display_name AS DisplayName,
                u.email_address AS EmailAddress,
                u.lifecycle_status AS LifecycleStatus,
                u.department_code AS DepartmentCode,
                u.aggregate_version AS Version
            FROM projection_user_directory u
            WHERE u.user_id = @userId;
            """,
            new { userId });

        return row is null
            ? null
            : new UserDetailReadModel(
                row.Id,
                row.EmployeeNumber,
                row.DisplayName,
                row.EmailAddress,
                row.DepartmentCode,
                row.LifecycleStatus.ToLowerInvariant(),
                row.Version);
    }

    public async Task<CursorPage<UserListItemReadModel>> ListAsync(
        ListUsersQuery query,
        CancellationToken ct)
    {
        var sortToken = query.Sort switch
        {
            UserSortOrder.DisplayNameAsc => "displayName",
            UserSortOrder.DisplayNameDesc => "-displayName",
            UserSortOrder.EmployeeNumberAsc => "employeeNumber",
            UserSortOrder.EmployeeNumberDesc => "-employeeNumber",
            _ => throw new ArgumentOutOfRangeException(nameof(query.Sort))
        };

        var orderBy = query.Sort switch
        {
            UserSortOrder.DisplayNameAsc => "u.display_name ASC, u.user_id ASC",
            UserSortOrder.DisplayNameDesc => "u.display_name DESC, u.user_id ASC",
            UserSortOrder.EmployeeNumberAsc => "u.employee_number ASC, u.user_id ASC",
            UserSortOrder.EmployeeNumberDesc => "u.employee_number DESC, u.user_id ASC",
            _ => throw new ArgumentOutOfRangeException(nameof(query.Sort))
        };

        UserCursor? cursor = null;
        if (readModelHelpers.TryDecodeCursor<UserCursor>(query.Cursor, out var parsedCursor)
            && string.Equals(parsedCursor?.Sort, sortToken, StringComparison.Ordinal))
        {
            cursor = parsedCursor;
        }

        var sortByEmployeeNumber = query.Sort is UserSortOrder.EmployeeNumberAsc or UserSortOrder.EmployeeNumberDesc;
        var sortDescending = query.Sort is UserSortOrder.DisplayNameDesc or UserSortOrder.EmployeeNumberDesc;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = (await connection.QueryAsync<ListRow>(
            $"""
            SELECT
                u.user_id AS Id,
                u.employee_number AS EmployeeNumber,
                u.display_name AS DisplayName,
                u.lifecycle_status AS LifecycleStatus,
                u.department_code AS DepartmentCode,
                u.aggregate_version AS Version
            FROM projection_user_directory u
            WHERE (
                @query IS NULL
                OR u.employee_number ILIKE @likeQuery
                OR u.display_name ILIKE @likeQuery
                OR COALESCE(u.department_code, '') ILIKE @likeQuery
            )
              AND (
                @status IS NULL
                OR u.lifecycle_status = @status
              )
              AND (
                @hasCursor = false
                OR (
                    @sortByEmployeeNumber = false
                    AND @sortDescending = false
                    AND (
                        u.display_name > @cursorDisplayName
                        OR (u.display_name = @cursorDisplayName AND u.user_id > @cursorId)
                    )
                )
                OR (
                    @sortByEmployeeNumber = false
                    AND @sortDescending = true
                    AND (
                        u.display_name < @cursorDisplayName
                        OR (u.display_name = @cursorDisplayName AND u.user_id > @cursorId)
                    )
                )
                OR (
                    @sortByEmployeeNumber = true
                    AND @sortDescending = false
                    AND (
                        u.employee_number > @cursorEmployeeNumber
                        OR (u.employee_number = @cursorEmployeeNumber AND u.user_id > @cursorId)
                    )
                )
                OR (
                    @sortByEmployeeNumber = true
                    AND @sortDescending = true
                    AND (
                        u.employee_number < @cursorEmployeeNumber
                        OR (u.employee_number = @cursorEmployeeNumber AND u.user_id > @cursorId)
                    )
                )
              )
            ORDER BY {orderBy}
            LIMIT @take;
            """,
            new
            {
                query = string.IsNullOrWhiteSpace(query.Query) ? null : query.Query.Trim(),
                likeQuery = $"%{query.Query?.Trim()}%",
                status = query.Status?.ToString(),
                hasCursor = cursor is not null,
                sortByEmployeeNumber,
                sortDescending,
                cursorDisplayName = cursor?.DisplayName,
                cursorEmployeeNumber = cursor?.EmployeeNumber,
                cursorId = cursor?.Id ?? Guid.Empty,
                take = query.Limit + 1
            })).ToArray();

        var hasNext = rows.Length > query.Limit;
        var pageRows = rows.Take(query.Limit).ToArray();
        var items = pageRows
            .Select(row => new UserListItemReadModel(
                row.Id,
                row.EmployeeNumber,
                row.DisplayName,
                row.LifecycleStatus.ToLowerInvariant(),
                row.DepartmentCode,
                row.Version))
            .ToArray();

        var nextCursor = hasNext && pageRows.Length > 0
            ? readModelHelpers.EncodeCursor(new UserCursor(
                sortToken,
                pageRows[^1].EmployeeNumber,
                pageRows[^1].DisplayName,
                pageRows[^1].Id))
            : null;

        return new CursorPage<UserListItemReadModel>(items, query.Limit, hasNext, nextCursor);
    }

    private sealed record UserCursor(string Sort, string EmployeeNumber, string DisplayName, Guid Id);
    private sealed record ListRow(Guid Id, string EmployeeNumber, string DisplayName, string LifecycleStatus, string? DepartmentCode, long Version);
    private sealed record DetailRow(
        Guid Id,
        string EmployeeNumber,
        string DisplayName,
        string? EmailAddress,
        string LifecycleStatus,
        string? DepartmentCode,
        long Version);
}

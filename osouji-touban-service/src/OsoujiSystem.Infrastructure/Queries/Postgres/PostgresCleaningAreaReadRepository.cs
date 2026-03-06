using Dapper;
using Npgsql;
using OsoujiSystem.Application.Queries.Abstractions;
using OsoujiSystem.Application.Queries.CleaningAreas;
using OsoujiSystem.Application.Queries.Shared;

namespace OsoujiSystem.Infrastructure.Queries.Postgres;

internal sealed class PostgresCleaningAreaReadRepository(
    NpgsqlDataSource dataSource) : ICleaningAreaReadRepository
{
    public async Task<CursorPage<CleaningAreaListItemReadModel>> ListAsync(
        ListCleaningAreasQuery query,
        CancellationToken ct)
    {
        var sortToken = query.Sort switch
        {
            CleaningAreaSortOrder.NameAsc => "name",
            CleaningAreaSortOrder.NameDesc => "-name",
            _ => throw new ArgumentOutOfRangeException(nameof(query.Sort))
        };
        var sortDescending = query.Sort == CleaningAreaSortOrder.NameDesc;

        CleaningAreaCursor? cursor = null;
        if (PostgresReadModelHelpers.TryDecodeCursor<CleaningAreaCursor>(query.Cursor, out var parsedCursor)
            && string.Equals(parsedCursor?.Sort, sortToken, StringComparison.Ordinal))
        {
            cursor = parsedCursor;
        }

        var orderBy = sortDescending
            ? "a.area_name DESC, a.area_id ASC"
            : "a.area_name ASC, a.area_id ASC";

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = (await connection.QueryAsync<ListRow>(
            $"""
            SELECT
                a.area_id AS Id,
                a.area_name AS Name,
                a.current_week_rule::text AS CurrentWeekRuleJson,
                (
                    SELECT COUNT(*)
                    FROM projection_area_members m
                    WHERE m.area_id = a.area_id
                      AND m.is_active = true
                ) AS MemberCount,
                (
                    SELECT COUNT(*)
                    FROM projection_cleaning_area_spots s
                    WHERE s.area_id = a.area_id
                ) AS SpotCount,
                a.aggregate_version AS Version
            FROM projection_cleaning_areas a
            WHERE (@userId IS NULL OR EXISTS (
                SELECT 1
                FROM projection_area_members m
                WHERE m.area_id = a.area_id
                  AND m.user_id = @userId
                  AND m.is_active = true
            ))
              AND (
                @hasCursor = false
                OR (
                    @sortDescending = false
                    AND (
                        a.area_name > @cursorName
                        OR (a.area_name = @cursorName AND a.area_id > @cursorId)
                    )
                )
                OR (
                    @sortDescending = true
                    AND (
                        a.area_name < @cursorName
                        OR (a.area_name = @cursorName AND a.area_id > @cursorId)
                    )
                )
              )
            ORDER BY {orderBy}
            LIMIT @take;
            """,
            new
            {
                userId = query.UserId,
                hasCursor = cursor is not null,
                sortDescending,
                cursorName = cursor?.Name,
                cursorId = cursor?.Id ?? Guid.Empty,
                take = query.Limit + 1
            })).ToArray();

        var hasNext = rows.Length > query.Limit;
        var pageRows = rows.Take(query.Limit).ToArray();
        var items = pageRows
            .Select(row => new CleaningAreaListItemReadModel(
                row.Id,
                row.Name,
                PostgresReadModelHelpers.DeserializeWeekRule(row.CurrentWeekRuleJson),
                row.MemberCount,
                row.SpotCount,
                row.Version))
            .ToArray();

        var nextCursor = hasNext && pageRows.Length > 0
            ? PostgresReadModelHelpers.EncodeCursor(new CleaningAreaCursor(
                sortToken,
                pageRows[^1].Name,
                pageRows[^1].Id))
            : null;

        return new CursorPage<CleaningAreaListItemReadModel>(items, query.Limit, hasNext, nextCursor);
    }

    public async Task<CleaningAreaDetailReadModel?> FindByIdAsync(Guid areaId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var header = await connection.QuerySingleOrDefaultAsync<DetailHeaderRow>(
            """
            SELECT
                area_id AS Id,
                area_name AS Name,
                current_week_rule::text AS CurrentWeekRuleJson,
                pending_week_rule::text AS PendingWeekRuleJson,
                rotation_cursor AS RotationCursor,
                aggregate_version AS Version
            FROM projection_cleaning_areas
            WHERE area_id = @areaId;
            """,
            new { areaId });

        if (header is null)
        {
            return null;
        }

        var spots = (await connection.QueryAsync<SpotRow>(
            """
            SELECT
                spot_id AS Id,
                spot_name AS Name,
                sort_order AS SortOrder
            FROM projection_cleaning_area_spots
            WHERE area_id = @areaId
            ORDER BY sort_order ASC, spot_name ASC, spot_id ASC;
            """,
            new { areaId }))
            .Select(row => new CleaningSpotReadModel(row.Id, row.Name, row.SortOrder))
            .ToArray();

        var members = (await connection.QueryAsync<MemberRow>(
            """
            SELECT
                area_member_id AS Id,
                user_id AS UserId,
                employee_number AS EmployeeNumber
            FROM projection_area_members
            WHERE area_id = @areaId
              AND is_active = true
            ORDER BY employee_number ASC, user_id ASC;
            """,
            new { areaId }))
            .Select(row => new AreaMemberReadModel(row.Id, row.UserId, row.EmployeeNumber))
            .ToArray();

        return new CleaningAreaDetailReadModel(
            header.Id,
            header.Name,
            PostgresReadModelHelpers.DeserializeWeekRule(header.CurrentWeekRuleJson),
            PostgresReadModelHelpers.DeserializeWeekRuleOrNull(header.PendingWeekRuleJson),
            header.RotationCursor,
            spots,
            members,
            header.Version);
    }

    private sealed record CleaningAreaCursor(string Sort, string Name, Guid Id);
    private sealed record ListRow(Guid Id, string Name, string CurrentWeekRuleJson, long MemberCount, long SpotCount, long Version);
    private sealed record DetailHeaderRow(Guid Id, string Name, string CurrentWeekRuleJson, string? PendingWeekRuleJson, int RotationCursor, long Version);
    private sealed record SpotRow(Guid Id, string Name, int SortOrder);
    private sealed record MemberRow(Guid Id, Guid UserId, string EmployeeNumber);
}

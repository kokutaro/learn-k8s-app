using Dapper;
using Npgsql;
using OsoujiSystem.Application.Queries.Abstractions;
using OsoujiSystem.Application.Queries.Facilities;
using OsoujiSystem.Application.Queries.Shared;

namespace OsoujiSystem.Infrastructure.Queries.Postgres;

internal sealed class PostgresFacilityReadRepository(
    NpgsqlDataSource dataSource,
    PostgresReadModelHelpers readModelHelpers) : IFacilityReadRepository
{
    public async Task<CursorPage<FacilityListItemReadModel>> ListAsync(
        ListFacilitiesQuery query,
        CancellationToken ct)
    {
        var sortToken = query.Sort switch
        {
            FacilitySortOrder.NameAsc => "name",
            FacilitySortOrder.NameDesc => "-name",
            FacilitySortOrder.FacilityCodeAsc => "facilityCode",
            FacilitySortOrder.FacilityCodeDesc => "-facilityCode",
            _ => throw new ArgumentOutOfRangeException(nameof(query.Sort))
        };

        var orderBy = query.Sort switch
        {
            FacilitySortOrder.NameAsc => "f.name ASC, f.facility_id ASC",
            FacilitySortOrder.NameDesc => "f.name DESC, f.facility_id ASC",
            FacilitySortOrder.FacilityCodeAsc => "f.facility_code ASC, f.facility_id ASC",
            FacilitySortOrder.FacilityCodeDesc => "f.facility_code DESC, f.facility_id ASC",
            _ => throw new ArgumentOutOfRangeException(nameof(query.Sort))
        };

        FacilityCursor? cursor = null;
        if (readModelHelpers.TryDecodeCursor<FacilityCursor>(query.Cursor, out var parsedCursor)
            && string.Equals(parsedCursor?.Sort, sortToken, StringComparison.Ordinal))
        {
            cursor = parsedCursor;
        }

        var sortByCode = query.Sort is FacilitySortOrder.FacilityCodeAsc or FacilitySortOrder.FacilityCodeDesc;
        var sortDescending = query.Sort is FacilitySortOrder.NameDesc or FacilitySortOrder.FacilityCodeDesc;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = (await connection.QueryAsync<ListRow>(
            $"""
            SELECT
                f.facility_id AS Id,
                f.facility_code AS FacilityCode,
                f.name AS Name,
                f.time_zone_id AS TimeZoneId,
                f.lifecycle_status AS LifecycleStatus,
                f.aggregate_version AS Version
            FROM projection_facilities f
            WHERE (
                @query IS NULL
                OR f.facility_code ILIKE @likeQuery
                OR f.name ILIKE @likeQuery
            )
              AND (
                @status IS NULL
                OR f.lifecycle_status = @status
              )
              AND (
                @hasCursor = false
                OR (
                    @sortByCode = false
                    AND @sortDescending = false
                    AND (
                        f.name > @cursorName
                        OR (f.name = @cursorName AND f.facility_id > @cursorId)
                    )
                )
                OR (
                    @sortByCode = false
                    AND @sortDescending = true
                    AND (
                        f.name < @cursorName
                        OR (f.name = @cursorName AND f.facility_id > @cursorId)
                    )
                )
                OR (
                    @sortByCode = true
                    AND @sortDescending = false
                    AND (
                        f.facility_code > @cursorFacilityCode
                        OR (f.facility_code = @cursorFacilityCode AND f.facility_id > @cursorId)
                    )
                )
                OR (
                    @sortByCode = true
                    AND @sortDescending = true
                    AND (
                        f.facility_code < @cursorFacilityCode
                        OR (f.facility_code = @cursorFacilityCode AND f.facility_id > @cursorId)
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
                sortByCode,
                sortDescending,
                cursorName = cursor?.Name,
                cursorFacilityCode = cursor?.FacilityCode,
                cursorId = cursor?.Id ?? Guid.Empty,
                take = query.Limit + 1
            })).ToArray();

        var hasNext = rows.Length > query.Limit;
        var pageRows = rows.Take(query.Limit).ToArray();
        var items = pageRows
            .Select(row => new FacilityListItemReadModel(
                row.Id,
                row.FacilityCode,
                row.Name,
                row.TimeZoneId,
                row.LifecycleStatus.ToLowerInvariant(),
                row.Version))
            .ToArray();

        var nextCursor = hasNext && pageRows.Length > 0
            ? readModelHelpers.EncodeCursor(new FacilityCursor(
                sortToken,
                pageRows[^1].FacilityCode,
                pageRows[^1].Name,
                pageRows[^1].Id))
            : null;

        return new CursorPage<FacilityListItemReadModel>(items, query.Limit, hasNext, nextCursor);
    }

    public async Task<FacilityDetailReadModel?> FindByIdAsync(Guid facilityId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<DetailRow>(
            """
            SELECT
                facility_id AS Id,
                facility_code AS FacilityCode,
                name AS Name,
                description AS Description,
                time_zone_id AS TimeZoneId,
                lifecycle_status AS LifecycleStatus,
                aggregate_version AS Version
            FROM projection_facilities
            WHERE facility_id = @facilityId;
            """,
            new { facilityId });

        return row is null
            ? null
            : new FacilityDetailReadModel(
                row.Id,
                row.FacilityCode,
                row.Name,
                row.Description,
                row.TimeZoneId,
                row.LifecycleStatus.ToLowerInvariant(),
                row.Version);
    }

    private sealed record FacilityCursor(string Sort, string FacilityCode, string Name, Guid Id);
    private sealed record ListRow(Guid Id, string FacilityCode, string Name, string TimeZoneId, string LifecycleStatus, long Version);
    private sealed record DetailRow(Guid Id, string FacilityCode, string Name, string? Description, string TimeZoneId, string LifecycleStatus, long Version);
}

using Dapper;
using Npgsql;
using OsoujiSystem.Application.Queries.Abstractions;
using OsoujiSystem.Application.Queries.Shared;
using OsoujiSystem.Application.Queries.WeeklyDutyPlans;

namespace OsoujiSystem.Infrastructure.Queries.Postgres;

internal sealed class PostgresWeeklyDutyPlanReadRepository(
    NpgsqlDataSource dataSource) : IWeeklyDutyPlanReadRepository
{
    public async Task<CursorPage<WeeklyDutyPlanListItemReadModel>> ListAsync(
        ListWeeklyDutyPlansQuery query,
        CancellationToken ct)
    {
        var sortToken = query.Sort switch
        {
            WeeklyDutyPlanSortOrder.WeekIdAsc => "weekId",
            WeeklyDutyPlanSortOrder.WeekIdDesc => "-weekId",
            WeeklyDutyPlanSortOrder.CreatedAtAsc => "createdAt",
            WeeklyDutyPlanSortOrder.CreatedAtDesc => "-createdAt",
            _ => throw new ArgumentOutOfRangeException(nameof(query.Sort))
        };

        WeeklyDutyPlanCursor? cursor = null;
        if (PostgresReadModelHelpers.TryDecodeCursor<WeeklyDutyPlanCursor>(query.Cursor, out var parsedCursor)
            && string.Equals(parsedCursor?.Sort, sortToken, StringComparison.Ordinal))
        {
            cursor = parsedCursor;
        }

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = (await connection.QueryAsync<ListRow>(
            $"""
             SELECT
                 plan_id AS Id,
                 area_id AS AreaId,
                 week_year AS WeekYear,
                 week_number AS WeekNumber,
                 revision AS Revision,
                 status AS Status,
                 aggregate_version AS Version,
                 created_at AS CreatedAt
             FROM projection_weekly_plans
             WHERE (@areaId IS NULL OR area_id = @areaId)
               AND (@weekYear IS NULL OR (week_year = @weekYear AND week_number = @weekNumber))
               AND (@status IS NULL OR status = @status)
               AND (
                 @hasCursor = false
                 OR (
                     @sortMode = 'week_asc'
                     AND (
                         week_year > @cursorWeekYear
                         OR (week_year = @cursorWeekYear AND week_number > @cursorWeekNumber)
                         OR (week_year = @cursorWeekYear AND week_number = @cursorWeekNumber AND plan_id > @cursorId)
                     )
                 )
                 OR (
                     @sortMode = 'week_desc'
                     AND (
                         week_year < @cursorWeekYear
                         OR (week_year = @cursorWeekYear AND week_number < @cursorWeekNumber)
                         OR (week_year = @cursorWeekYear AND week_number = @cursorWeekNumber AND plan_id > @cursorId)
                     )
                 )
                 OR (
                     @sortMode = 'created_asc'
                     AND (
                         created_at > @cursorCreatedAt
                         OR (created_at = @cursorCreatedAt AND plan_id > @cursorId)
                     )
                 )
                 OR (
                     @sortMode = 'created_desc'
                     AND (
                         created_at < @cursorCreatedAt
                         OR (created_at = @cursorCreatedAt AND plan_id > @cursorId)
                     )
                 )
               )
             ORDER BY {GetOrderBy(query.Sort)}
             LIMIT @take;
             """,
            new
            {
                areaId = query.AreaId,
                weekYear = query.WeekId?.Year,
                weekNumber = query.WeekId?.WeekNumber,
                status = query.Status is null ? (short?)null : (short)query.Status.Value,
                hasCursor = cursor is not null,
                sortMode = GetSortMode(query.Sort),
                cursorWeekYear = cursor?.WeekYear,
                cursorWeekNumber = cursor?.WeekNumber,
                cursorCreatedAt = cursor?.CreatedAt,
                cursorId = cursor?.Id ?? Guid.Empty,
                take = query.Limit + 1
            })).ToArray();

        var hasNext = rows.Length > query.Limit;
        var pageRows = rows.Take(query.Limit).ToArray();
        var items = pageRows
            .Select(row => new WeeklyDutyPlanListItemReadModel(
                row.Id,
                row.AreaId,
                PostgresReadModelHelpers.ToWeekId(row.WeekYear, row.WeekNumber),
                row.Revision,
                PostgresReadModelHelpers.ToWeeklyPlanStatus(row.Status),
                row.Version,
                row.CreatedAt))
            .ToArray();

        var nextCursor = hasNext && pageRows.Length > 0
            ? EncodeCursor(sortToken, pageRows[^1])
            : null;

        return new CursorPage<WeeklyDutyPlanListItemReadModel>(items, query.Limit, hasNext, nextCursor);
    }

    public async Task<WeeklyDutyPlanDetailReadModel?> FindByIdAsync(Guid planId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var header = await connection.QuerySingleOrDefaultAsync<DetailHeaderRow>(
            """
            SELECT
                plan_id AS Id,
                area_id AS AreaId,
                week_year AS WeekYear,
                week_number AS WeekNumber,
                revision AS Revision,
                status AS Status,
                fairness_window_weeks AS FairnessWindowWeeks,
                aggregate_version AS Version
            FROM projection_weekly_plans
            WHERE plan_id = @planId;
            """,
            new { planId });

        if (header is null)
        {
            return null;
        }

        var assignments = (await connection.QueryAsync<AssignmentRow>(
                """
                SELECT
                    a.spot_id AS SpotId,
                    a.user_id AS UserId
                FROM projection_weekly_plan_assignments a
                LEFT JOIN projection_cleaning_area_spots s
                  ON s.area_id = @areaId
                 AND s.spot_id = a.spot_id
                WHERE a.plan_id = @planId
                ORDER BY s.sort_order NULLS LAST, a.spot_id;
                """,
                new { planId, areaId = header.AreaId }))
            .ToArray();

        var offDutyEntries = (await connection.QueryAsync<OffDutyRow>(
                """
                SELECT user_id AS UserId
                FROM projection_weekly_plan_offduty
                WHERE plan_id = @planId
                ORDER BY user_id;
                """,
                new { planId }))
            .ToArray();

        var userIds = assignments
            .Select(x => x.UserId)
            .Concat(offDutyEntries.Select(x => x.UserId))
            .Distinct()
            .ToArray();

        var usersById = userIds.Length == 0
            ? new Dictionary<Guid, WeeklyDutyPlanUserSummaryReadModel>()
            : (await connection.QueryAsync<UserDirectoryRow>(
                    """
                    SELECT
                        user_id AS UserId,
                        employee_number AS EmployeeNumber,
                        display_name AS DisplayName,
                        department_code AS DepartmentCode,
                        lifecycle_status AS LifecycleStatus
                    FROM projection_user_directory
                    WHERE user_id = ANY(@userIds);
                    """,
                    new { userIds }))
                .ToDictionary(
                    row => row.UserId,
                    row => new WeeklyDutyPlanUserSummaryReadModel(
                        row.UserId,
                        row.EmployeeNumber,
                        row.DisplayName,
                        row.DepartmentCode,
                        row.LifecycleStatus));

        return new WeeklyDutyPlanDetailReadModel(
            header.Id,
            header.AreaId,
            PostgresReadModelHelpers.ToWeekId(header.WeekYear, header.WeekNumber),
            header.Revision,
            PostgresReadModelHelpers.ToWeeklyPlanStatus(header.Status),
            new AssignmentPolicyReadModel(header.FairnessWindowWeeks),
            assignments
                .Select(row => new DutyAssignmentReadModel(
                    row.SpotId,
                    row.UserId,
                    usersById.GetValueOrDefault(row.UserId)))
                .ToArray(),
            offDutyEntries
                .Select(row => new OffDutyEntryReadModel(
                    row.UserId,
                    usersById.GetValueOrDefault(row.UserId)))
                .ToArray(),
            header.Version);
    }

    private static string EncodeCursor(string sortToken, ListRow row) => PostgresReadModelHelpers.EncodeCursor(
        new WeeklyDutyPlanCursor(
            sortToken,
            row.Id,
            row.WeekYear,
            row.WeekNumber,
            row.CreatedAt));

    private static string GetSortMode(WeeklyDutyPlanSortOrder sort) => sort switch
    {
        WeeklyDutyPlanSortOrder.WeekIdAsc => "week_asc",
        WeeklyDutyPlanSortOrder.WeekIdDesc => "week_desc",
        WeeklyDutyPlanSortOrder.CreatedAtAsc => "created_asc",
        WeeklyDutyPlanSortOrder.CreatedAtDesc => "created_desc",
        _ => throw new ArgumentOutOfRangeException(nameof(sort))
    };

    private static string GetOrderBy(WeeklyDutyPlanSortOrder sort) => sort switch
    {
        WeeklyDutyPlanSortOrder.WeekIdAsc => "week_year ASC, week_number ASC, plan_id ASC",
        WeeklyDutyPlanSortOrder.WeekIdDesc => "week_year DESC, week_number DESC, plan_id ASC",
        WeeklyDutyPlanSortOrder.CreatedAtAsc => "created_at ASC, plan_id ASC",
        WeeklyDutyPlanSortOrder.CreatedAtDesc => "created_at DESC, plan_id ASC",
        _ => throw new ArgumentOutOfRangeException(nameof(sort))
    };

    private sealed record WeeklyDutyPlanCursor(
        string Sort,
        Guid Id,
        int? WeekYear,
        int? WeekNumber,
        DateTimeOffset? CreatedAt);

    private sealed record ListRow(
        Guid Id,
        Guid AreaId,
        int WeekYear,
        int WeekNumber,
        int Revision,
        short Status,
        long Version,
        DateTime CreatedAt);

    private sealed record DetailHeaderRow(
        Guid Id,
        Guid AreaId,
        int WeekYear,
        int WeekNumber,
        int Revision,
        short Status,
        int FairnessWindowWeeks,
        long Version);

    private sealed record AssignmentRow(Guid SpotId, Guid UserId);

    private sealed record OffDutyRow(Guid UserId);

    private sealed record UserDirectoryRow(
        Guid UserId,
        string EmployeeNumber,
        string DisplayName,
        string? DepartmentCode,
        string LifecycleStatus);
}

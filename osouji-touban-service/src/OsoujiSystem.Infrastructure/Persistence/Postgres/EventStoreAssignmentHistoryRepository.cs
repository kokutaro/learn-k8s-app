using System.Globalization;
using Dapper;
using Npgsql;
using OsoujiSystem.Domain.DomainServices;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class EventStoreAssignmentHistoryRepository(
    NpgsqlDataSource dataSource,
    ITransactionContextAccessor transactionContextAccessor,
    IEventWriteContextAccessor eventWriteContextAccessor) : PostgresRepositoryBase(dataSource, transactionContextAccessor, eventWriteContextAccessor), IAssignmentHistoryRepository
{
  public Task<IReadOnlyDictionary<UserId, AssignmentHistorySnapshot>> GetSnapshotsAsync(
        CleaningAreaId areaId,
        WeekId targetWeek,
        int windowWeeks,
        IReadOnlyCollection<UserId> userIds,
        CancellationToken ct)
        => ExecuteReadAsync(async (connection, transaction) =>
        {
            if (windowWeeks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(windowWeeks));
            }

            if (userIds.Count == 0)
            {
                return new Dictionary<UserId, AssignmentHistorySnapshot>();
            }

            var weeks = GetPreviousWeeks(targetWeek, windowWeeks).ToArray();
            var minYear = weeks.Min(x => x.Year);
            var maxYear = weeks.Max(x => x.Year);

            var rows = await connection.QueryAsync<WorkloadRow>(
                """
                SELECT user_id AS UserId,
                       week_year AS WeekYear,
                       week_number AS WeekNumber,
                       assigned_count AS AssignedCount,
                       off_duty_count AS OffDutyCount
                FROM projection_user_weekly_workloads
                WHERE area_id = @areaId
                  AND user_id = ANY(@userIds)
                  AND week_year BETWEEN @minYear AND @maxYear;
                """,
                new
                {
                    areaId = areaId.Value,
                    userIds = userIds.Select(x => x.Value).ToArray(),
                    minYear,
                    maxYear
                },
                transaction: transaction);

            var rowMap = rows
                .Where(row => weeks.Any(week => week.Year == row.WeekYear && week.WeekNumber == row.WeekNumber))
                .ToDictionary(
                    row => (row.UserId, row.WeekYear, row.WeekNumber),
                    row => row);

            var result = new Dictionary<UserId, AssignmentHistorySnapshot>(userIds.Count);
            foreach (var userId in userIds)
            {
                var assignedCount = weeks
                    .Select(week => rowMap.GetValueOrDefault((userId.Value, week.Year, week.WeekNumber))?.AssignedCount ?? 0)
                    .Sum();

                var consecutiveOffDutyWeeks = 0;
                foreach (var week in weeks)
                {
                    var offDuty = rowMap.GetValueOrDefault((userId.Value, week.Year, week.WeekNumber))?.OffDutyCount ?? 0;
                    if (offDuty <= 0)
                    {
                        break;
                    }

                    consecutiveOffDutyWeeks++;
                }

                result[userId] = new AssignmentHistorySnapshot(userId, assignedCount, consecutiveOffDutyWeeks);
            }

            return (IReadOnlyDictionary<UserId, AssignmentHistorySnapshot>)result;
        }, ct);

    private static IEnumerable<WeekId> GetPreviousWeeks(WeekId targetWeek, int windowWeeks)
    {
        var current = ISOWeek.ToDateTime(targetWeek.Year, targetWeek.WeekNumber, DayOfWeek.Monday).AddDays(-7);
        for (var i = 0; i < windowWeeks; i++)
        {
            yield return new WeekId(ISOWeek.GetYear(current), ISOWeek.GetWeekOfYear(current));
            current = current.AddDays(-7);
        }
    }

    private sealed record WorkloadRow(Guid UserId, int WeekYear, int WeekNumber, int AssignedCount, int OffDutyCount);
}

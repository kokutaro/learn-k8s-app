using AwesomeAssertions;
using OsoujiSystem.Application.UseCases.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class AutoSchedulePlanServiceTests
{
    [Fact]
    public void ComputeCurrentWeekId_ShouldReturnCorrectWeek_WhenUtcTimeZone()
    {
        var weekRule = WeekRule.Create(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            "UTC",
            new WeekId(2026, 1)).Value;

        var utcNow = new DateTimeOffset(2026, 3, 11, 10, 0, 0, TimeSpan.Zero); // Wednesday W11

        var result = AutoSchedulePlanService.ComputeCurrentWeekId(weekRule, utcNow);

        result.Year.Should().Be(2026);
        result.WeekNumber.Should().Be(11);
    }

    [Fact]
    public void ComputeCurrentWeekId_ShouldRespectTimeZone_WhenTokyoTimeZone()
    {
        var weekRule = WeekRule.Create(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            "Asia/Tokyo",
            new WeekId(2026, 1)).Value;

        // UTC 15:00 Sunday = JST 00:00 Monday (next week)
        var utcNow = new DateTimeOffset(2026, 3, 8, 15, 0, 0, TimeSpan.Zero);

        var result = AutoSchedulePlanService.ComputeCurrentWeekId(weekRule, utcNow);

        // In JST, it's already Monday March 9 → ISO week 11
        result.Year.Should().Be(2026);
        result.WeekNumber.Should().Be(11);
    }

    [Fact]
    public void HasWeekStarted_ShouldReturnTrue_WhenPastStartDayAndTime()
    {
        var weekRule = WeekRule.Create(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            "UTC",
            new WeekId(2026, 1)).Value;

        var weekId = new WeekId(2026, 11);
        // Monday 2026-03-09 10:00 UTC (after 09:00 Monday)
        var utcNow = new DateTimeOffset(2026, 3, 9, 10, 0, 0, TimeSpan.Zero);

        var result = AutoSchedulePlanService.HasWeekStarted(weekRule, weekId, utcNow);

        result.Should().BeTrue();
    }

    [Fact]
    public void HasWeekStarted_ShouldReturnFalse_WhenBeforeStartTime()
    {
        var weekRule = WeekRule.Create(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            "UTC",
            new WeekId(2026, 1)).Value;

        var weekId = new WeekId(2026, 11);
        // Monday 2026-03-09 08:00 UTC (before 09:00 Monday)
        var utcNow = new DateTimeOffset(2026, 3, 9, 8, 0, 0, TimeSpan.Zero);

        var result = AutoSchedulePlanService.HasWeekStarted(weekRule, weekId, utcNow);

        result.Should().BeFalse();
    }

    [Fact]
    public void HasWeekStarted_ShouldReturnTrue_WhenStartDayIsFridayAndPastFriday()
    {
        var weekRule = WeekRule.Create(
            DayOfWeek.Friday,
            new TimeOnly(10, 0),
            "UTC",
            new WeekId(2026, 1)).Value;

        var weekId = new WeekId(2026, 11);
        // Friday 2026-03-13 10:30 UTC (ISO week 11, Friday at 10:30)
        var utcNow = new DateTimeOffset(2026, 3, 13, 10, 30, 0, TimeSpan.Zero);

        var result = AutoSchedulePlanService.HasWeekStarted(weekRule, weekId, utcNow);

        result.Should().BeTrue();
    }

    [Fact]
    public void HasWeekStarted_ShouldReturnFalse_WhenStartDayIsFridayButStillThursday()
    {
        var weekRule = WeekRule.Create(
            DayOfWeek.Friday,
            new TimeOnly(10, 0),
            "UTC",
            new WeekId(2026, 1)).Value;

        var weekId = new WeekId(2026, 11);
        // Thursday 2026-03-12 23:59 UTC (before Friday 10:00 of ISO week 11)
        var utcNow = new DateTimeOffset(2026, 3, 12, 23, 59, 0, TimeSpan.Zero);

        var result = AutoSchedulePlanService.HasWeekStarted(weekRule, weekId, utcNow);

        result.Should().BeFalse();
    }

    [Fact]
    public void HasWeekStarted_ShouldRespectTimeZone_WhenTokyoTimeZone()
    {
        var weekRule = WeekRule.Create(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            "Asia/Tokyo",
            new WeekId(2026, 1)).Value;

        var weekId = new WeekId(2026, 11);
        // UTC 00:00 Monday = JST 09:00 Monday → exactly at start
        var utcNow = new DateTimeOffset(2026, 3, 9, 0, 0, 0, TimeSpan.Zero);

        var result = AutoSchedulePlanService.HasWeekStarted(weekRule, weekId, utcNow);

        result.Should().BeTrue();
    }

    [Fact]
    public void HasWeekStarted_ShouldReturnFalse_WhenTokyoTimeZoneAndBeforeStart()
    {
        var weekRule = WeekRule.Create(
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            "Asia/Tokyo",
            new WeekId(2026, 1)).Value;

        var weekId = new WeekId(2026, 11);
        // UTC 23:59 Sunday = JST 08:59 Monday → just before start
        var utcNow = new DateTimeOffset(2026, 3, 8, 23, 59, 0, TimeSpan.Zero);

        var result = AutoSchedulePlanService.HasWeekStarted(weekRule, weekId, utcNow);

        result.Should().BeFalse();
    }
}

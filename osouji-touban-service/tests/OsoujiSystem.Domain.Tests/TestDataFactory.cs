using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Tests;

internal static class TestDataFactory
{
    public static WeekId Week(int year = 2026, int weekNumber = 10)
    {
        var result = WeekId.Create(year, weekNumber);
        return result.Value;
    }

    public static WeekRule WeekRule(
        DayOfWeek startDay = DayOfWeek.Monday,
        int hour = 0,
        string? timeZoneId = null,
        WeekId? effectiveFromWeek = null)
    {
        var result = OsoujiSystem.Domain.ValueObjects.WeekRule.Create(
            startDay,
            new TimeOnly(hour, 0),
            timeZoneId ?? TimeZoneInfo.Utc.Id,
            effectiveFromWeek ?? Week());

        return result.Value;
    }

    public static EmployeeNumber EmployeeNo(string value)
    {
        return EmployeeNumber.Create(value).Value;
    }

    public static CleaningSpot Spot(string name, int sortOrder)
    {
        return new CleaningSpot(CleaningSpotId.New(), name, sortOrder);
    }

    public static AreaMember Member(string employeeNumber)
    {
        return new AreaMember(AreaMemberId.New(), UserId.New(), EmployeeNo(employeeNumber));
    }

    public static DutyAssignment Assignment(CleaningSpotId spotId, UserId userId)
    {
        return new DutyAssignment(spotId, userId);
    }

    public static OffDutyEntry OffDuty(UserId userId)
    {
        return new OffDutyEntry(userId);
    }
}

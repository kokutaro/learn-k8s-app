using System.Globalization;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Errors;

namespace OsoujiSystem.Domain.ValueObjects;

public readonly record struct WeekId(int Year, int WeekNumber) : IComparable<WeekId>
{
    public static Result<WeekId, DomainError> Create(int year, int weekNumber)
    {
        if (year < 1 || weekNumber is < 1 or > 53)
        {
            return Result<WeekId, DomainError>.Failure(new InvalidWeekIdError(year, weekNumber));
        }

        return Result<WeekId, DomainError>.Success(new WeekId(year, weekNumber));
    }

    public static WeekId FromDate(DateOnly date)
    {
        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        return new WeekId(
            ISOWeek.GetYear(dateTime),
            ISOWeek.GetWeekOfYear(dateTime));
    }

    public DateOnly GetStartDate(DayOfWeek firstDayOfWeek)
    {
        var isoMonday = ISOWeek.ToDateTime(Year, WeekNumber, DayOfWeek.Monday);
        var offset = ((int)firstDayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return DateOnly.FromDateTime(isoMonday.AddDays(offset));
    }

    public int CompareTo(WeekId other)
    {
        var yearCompare = Year.CompareTo(other.Year);
        return yearCompare != 0 ? yearCompare : WeekNumber.CompareTo(other.WeekNumber);
    }

    public override string ToString() => $"{Year:0000}-W{WeekNumber:00}";
}

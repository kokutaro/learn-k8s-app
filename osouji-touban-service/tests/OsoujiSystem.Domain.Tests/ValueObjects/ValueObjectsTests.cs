using System.Globalization;
using AwesomeAssertions;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Tests.ValueObjects;

public sealed class EmployeeNumberTests
{
    [Fact]
    public void Create_WhenValueIsWhitespace_ShouldFail()
    {
        // Arrange
        const string value = "   ";

        // Act
        var result = EmployeeNumber.Create(value);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidEmployeeNumberError>();
    }

    [Fact]
    public void Create_WhenValueHasPadding_ShouldTrimAndSucceed()
    {
        // Arrange
        const string value = "  0007  ";

        // Act
        var result = EmployeeNumber.Create(value);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be("0007");
    }

    [Fact]
    public void CompareTo_WhenCompared_ShouldUseOrdinalOrder()
    {
        // Arrange
        var left = EmployeeNumber.Create("0001").Value;
        var right = EmployeeNumber.Create("0010").Value;

        // Act
        var compareResult = left.CompareTo(right);

        // Assert
        compareResult.Should().BeLessThan(0);
    }
}

public sealed class WeekIdTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(2026, 0)]
    [InlineData(2026, 54)]
    public void Create_WhenOutOfRange_ShouldFail(int year, int weekNumber)
    {
        // Arrange

        // Act
        var result = WeekId.Create(year, weekNumber);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidWeekIdError>();
    }

    [Fact]
    public void Create_WhenBoundaryMin_ShouldSucceed()
    {
        // Arrange

        // Act
        var result = WeekId.Create(1, 1);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Year.Should().Be(1);
        result.Value.WeekNumber.Should().Be(1);
    }

    [Fact]
    public void CompareTo_WhenYearSame_ShouldCompareWeekNumber()
    {
        // Arrange
        var left = WeekId.Create(2026, 9).Value;
        var right = WeekId.Create(2026, 10).Value;

        // Act
        var compareResult = left.CompareTo(right);

        // Assert
        compareResult.Should().BeLessThan(0);
    }

    [Fact]
    public void FromDate_ShouldCreateIsoWeekId()
    {
        // Arrange
        var date = new DateOnly(2026, 3, 4);
        var expectedWeek = ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));
        var expectedYear = ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue));

        // Act
        var result = WeekId.FromDate(date);

        // Assert
        result.Year.Should().Be(expectedYear);
        result.WeekNumber.Should().Be(expectedWeek);
    }
}

public sealed class WeekRuleTests
{
    [Fact]
    public void Create_WhenTimeZoneIsWhitespace_ShouldFail()
    {
        // Arrange
        var week = WeekId.Create(2026, 10).Value;

        // Act
        var result = WeekRule.Create(DayOfWeek.Monday, new TimeOnly(0, 0), " ", week);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidWeekRuleTimeZoneError>();
    }

    [Fact]
    public void Create_WhenTimeZoneIsInvalid_ShouldFail()
    {
        // Arrange
        var week = WeekId.Create(2026, 10).Value;

        // Act
        var result = WeekRule.Create(DayOfWeek.Monday, new TimeOnly(0, 0), "Invalid/Zone", week);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidWeekRuleTimeZoneError>();
    }

    [Fact]
    public void Create_WhenInputIsValid_ShouldSucceed()
    {
        // Arrange
        var week = WeekId.Create(2026, 10).Value;
        var timeZoneId = TimeZoneInfo.Utc.Id;

        // Act
        var result = WeekRule.Create(DayOfWeek.Monday, new TimeOnly(0, 0), timeZoneId, week);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TimeZoneId.Should().Be(timeZoneId);
        result.Value.EffectiveFromWeek.Should().Be(week);
    }
}

public sealed class RotationCursorTests
{
    [Fact]
    public void Create_WhenNegativeValue_ShouldFail()
    {
        // Arrange

        // Act
        var result = RotationCursor.Create(-1);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidRotationCursorError>();
    }

    [Fact]
    public void Create_WhenZero_ShouldSucceed()
    {
        // Arrange

        // Act
        var result = RotationCursor.Create(0);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void MoveNext_WhenModuloIsNotPositive_ShouldKeepValue(int modulo)
    {
        // Arrange
        var cursor = new RotationCursor(3);

        // Act
        var next = cursor.MoveNext(modulo);

        // Assert
        next.Value.Should().Be(3);
    }

    [Fact]
    public void MoveNext_WhenAtBoundary_ShouldWrapAround()
    {
        // Arrange
        var cursor = new RotationCursor(2);

        // Act
        var next = cursor.MoveNext(3);

        // Assert
        next.Value.Should().Be(0);
    }
}

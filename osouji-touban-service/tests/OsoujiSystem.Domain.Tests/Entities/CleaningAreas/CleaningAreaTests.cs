using AwesomeAssertions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Tests.Entities.CleaningAreas;

public sealed class CleaningAreaTests
{
    [Fact]
    public void Register_WhenNameIsWhitespace_ShouldFail()
    {
        // Arrange
        var areaId = CleaningAreaId.New();
        var weekRule = TestDataFactory.WeekRule();

        // Act
        var result = CleaningArea.Register(areaId, " ", weekRule, [TestDataFactory.Spot("Initial", 999)]);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidWeekRuleError>();
    }

    [Fact]
    public void Register_WhenInputIsValid_ShouldSucceedAndPublishEvent()
    {
        // Arrange
        var areaId = CleaningAreaId.New();
        var weekRule = TestDataFactory.WeekRule();

        // Act
        var result = CleaningArea.Register(areaId, "  East Floor  ", weekRule, [TestDataFactory.Spot("Initial", 999)]);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("East Floor");
        result.Value.DomainEvents.Should().ContainSingle(e => e is CleaningAreaRegistered);
    }

    [Fact]
    public void Register_WhenInitialSpotsAreEmpty_ShouldFail()
    {
        // Arrange
        var areaId = CleaningAreaId.New();
        var weekRule = TestDataFactory.WeekRule();

        // Act
        var result = CleaningArea.Register(areaId, "East Floor", weekRule, []);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<CleaningAreaHasNoSpotError>();
    }

    [Fact]
    public void ScheduleWeekRuleChange_WhenEffectiveWeekIsCurrentOrPast_ShouldFail()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        area.ClearDomainEvents();
        var pastRule = TestDataFactory.WeekRule(effectiveFromWeek: TestDataFactory.Week(2026, 9));

        var currentRule = TestDataFactory.WeekRule(effectiveFromWeek: TestDataFactory.Week());

        // Act
        var currentResult = area.ScheduleWeekRuleChange(currentRule);
        var result = area.ScheduleWeekRuleChange(pastRule);

        // Assert
        currentResult.IsFailure.Should().BeTrue();
        currentResult.Error.Should().BeOfType<InvalidWeekRuleError>();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidWeekRuleError>();
    }

    [Fact]
    public void ScheduleWeekRuleChange_WhenFutureWeek_ShouldSetPendingAndPublishEvent()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        area.ClearDomainEvents();
        var futureRule = TestDataFactory.WeekRule(effectiveFromWeek: TestDataFactory.Week(2026, 11));

        // Act
        var result = area.ScheduleWeekRuleChange(futureRule);

        // Assert
        result.IsSuccess.Should().BeTrue();
        area.PendingWeekRule.Should().Be(futureRule);
        area.DomainEvents.Should().ContainSingle(e => e is WeekRuleChangeScheduled);
    }

    [Fact]
    public void ApplyPendingWeekRule_WhenNoPending_ShouldNotChangeCurrentRule()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        var before = area.CurrentWeekRule;

        // Act
        var result = area.ApplyPendingWeekRule(TestDataFactory.Week());

        // Assert
        result.IsSuccess.Should().BeTrue();
        area.CurrentWeekRule.Should().Be(before);
    }

    [Fact]
    public void ApplyPendingWeekRule_WhenEffectiveWeekIsFuture_ShouldKeepPending()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        var pendingRule = TestDataFactory.WeekRule(effectiveFromWeek: TestDataFactory.Week(2026, 12));
        area.ScheduleWeekRuleChange(pendingRule);

        // Act
        var result = area.ApplyPendingWeekRule(TestDataFactory.Week(2026, 11));

        // Assert
        result.IsSuccess.Should().BeTrue();
        area.PendingWeekRule.Should().Be(pendingRule);
        area.CurrentWeekRule.EffectiveFromWeek.Should().Be(TestDataFactory.Week());
    }

    [Fact]
    public void ApplyPendingWeekRule_WhenEffectiveWeekArrived_ShouldPromotePendingRule()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        var pendingRule = TestDataFactory.WeekRule(effectiveFromWeek: TestDataFactory.Week(2026, 11));
        area.ScheduleWeekRuleChange(pendingRule);

        // Act
        var result = area.ApplyPendingWeekRule(TestDataFactory.Week(2026, 11));

        // Assert
        result.IsSuccess.Should().BeTrue();
        area.CurrentWeekRule.Should().Be(pendingRule);
        area.PendingWeekRule.Should().BeNull();
    }

    [Fact]
    public void AddSpot_WhenDuplicateId_ShouldFail()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        var spotId = CleaningSpotId.New();
        area.AddSpot(new CleaningSpot(spotId, "Hall", 1));
        area.ClearDomainEvents();

        // Act
        var result = area.AddSpot(new CleaningSpot(spotId, "Hall B", 2));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<DuplicateCleaningSpotError>();
    }

    [Fact]
    public void AddSpot_WhenDifferentSortAndName_ShouldKeepDeterministicOrder()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        area.AddSpot(new CleaningSpot(CleaningSpotId.New(), "B", 2));
        area.ClearDomainEvents();

        // Act
        var add1 = area.AddSpot(new CleaningSpot(CleaningSpotId.New(), "C", 1));
        var add2 = area.AddSpot(new CleaningSpot(CleaningSpotId.New(), "A", 1));

        // Assert
        add1.IsSuccess.Should().BeTrue();
        add2.IsSuccess.Should().BeTrue();
        area.Spots.Select(x => x.Name).Should().ContainInOrder("A", "C", "B");
        area.DomainEvents.Count(e => e is CleaningSpotAdded).Should().Be(2);
    }

    [Fact]
    public void RemoveSpot_WhenSpotNotFound_ShouldSucceedAndKeepCurrentSpots()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        area.AddSpot(TestDataFactory.Spot("A", 1));
        area.AddSpot(TestDataFactory.Spot("B", 2));
        area.ClearDomainEvents();
        var beforeCount = area.Spots.Count;

        // Act
        var result = area.RemoveSpot(CleaningSpotId.New());

        // Assert
        result.IsSuccess.Should().BeTrue();
        area.Spots.Count.Should().Be(beforeCount);
        area.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void RemoveSpot_WhenOnlyOneSpotExists_ShouldFail()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        area.ClearDomainEvents();
        var onlySpot = area.Spots.Single();

        // Act
        var result = area.RemoveSpot(onlySpot.Id);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<CleaningAreaHasNoSpotError>();
    }

    [Fact]
    public void RemoveSpot_WhenMultipleSpotsExist_ShouldRemoveAndPublishEvent()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        var spotA = TestDataFactory.Spot("A", 1);
        var spotB = TestDataFactory.Spot("B", 2);
        area.AddSpot(spotA);
        area.AddSpot(spotB);
        area.ClearDomainEvents();

        // Act
        var result = area.RemoveSpot(spotA.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        area.Spots.Select(x => x.Id).Should().NotContain(spotA.Id);
        area.DomainEvents.Should().ContainSingle(e => e is CleaningSpotRemoved);
    }

    [Fact]
    public void AssignUser_WhenDuplicateUser_ShouldFail()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        var userId = UserId.New();
        var first = new AreaMember(AreaMemberId.New(), userId, TestDataFactory.EmployeeNo("000002"));
        area.AssignUser(first);
        area.ClearDomainEvents();

        // Act
        var result = area.AssignUser(new AreaMember(AreaMemberId.New(), userId, TestDataFactory.EmployeeNo("000001")));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<DuplicateAreaMemberError>();
    }

    [Fact]
    public void AssignUser_WhenEmployeeNumberOrderDiffers_ShouldSortByEmployeeNumber()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        area.AssignUser(new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000005")));
        area.ClearDomainEvents();

        // Act
        var result = area.AssignUser(new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001")));

        // Assert
        result.IsSuccess.Should().BeTrue();
        area.Members.Select(x => x.EmployeeNumber.Value).Should().ContainInOrder("000001", "000005");
        area.DomainEvents.Should().ContainSingle(e => e is UserAssignedToArea);
    }

    [Fact]
    public void UnassignUser_WhenUserNotFound_ShouldSucceedWithoutEvent()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        area.ClearDomainEvents();

        // Act
        var result = area.UnassignUser(UserId.New());

        // Assert
        result.IsSuccess.Should().BeTrue();
        area.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void UnassignUser_WhenTransferAreaProvided_ShouldPublishUnassignAndTransferEvents()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        var member = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"));
        area.AssignUser(member);
        area.ClearDomainEvents();
        var transferAreaId = CleaningAreaId.New();

        // Act
        var result = area.UnassignUser(member.UserId, transferAreaId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        area.Members.Should().BeEmpty();
        area.DomainEvents.Should().ContainSingle(e => e is UserUnassignedFromArea);
        area.DomainEvents.Should().ContainSingle(e => e is UserTransferredFromArea);
    }

    [Fact]
    public void UpdateRotationCursor_WhenCalled_ShouldReplaceCursor()
    {
        // Arrange
        var area = CreateAreaWithWeek(2026, 10);
        var cursor = RotationCursor.Create(2).Value;

        // Act
        area.UpdateRotationCursor(cursor);

        // Assert
        area.RotationCursor.Should().Be(cursor);
    }

    private static CleaningArea CreateAreaWithWeek(int year, int weekNumber)
    {
        var week = WeekId.Create(year, weekNumber).Value;
        var rule = TestDataFactory.WeekRule(effectiveFromWeek: week);
        return CleaningArea.Register(CleaningAreaId.New(), "Area", rule, [TestDataFactory.Spot("Initial", 999)]).Value;
    }
}

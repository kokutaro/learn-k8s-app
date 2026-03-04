using AwesomeAssertions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Tests.Entities.WeeklyDutyPlans;

public sealed class WeeklyDutyPlanTests
{
    [Fact]
    public void Generate_WhenDuplicateSpotAssignmentsExist_ShouldFail()
    {
        // Arrange
        var spotId = CleaningSpotId.New();
        var assignments = new[]
        {
            new DutyAssignment(spotId, UserId.New()),
            new DutyAssignment(spotId, UserId.New())
        };

        // Act
        var result = WeeklyDutyPlan.Generate(
            WeeklyDutyPlanId.New(),
            CleaningAreaId.New(),
            TestDataFactory.Week(),
            AssignmentPolicy.Default,
            assignments,
            []);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<AssignmentConflictError>();
    }

    [Fact]
    public void Generate_WhenValid_ShouldCreateDraftAndPublishInitialEvents()
    {
        // Arrange
        var assignments = new[]
        {
            new DutyAssignment(CleaningSpotId.New(), UserId.New()),
            new DutyAssignment(CleaningSpotId.New(), UserId.New())
        };
        var offDuties = new[] { new OffDutyEntry(UserId.New()) };

        // Act
        var result = WeeklyDutyPlan.Generate(
            WeeklyDutyPlanId.New(),
            CleaningAreaId.New(),
            TestDataFactory.Week(),
            AssignmentPolicy.Default,
            assignments,
            offDuties);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var plan = result.Value;
        plan.Status.Should().Be(WeeklyPlanStatus.Draft);
        plan.Revision.Should().Be(PlanRevision.Initial);
        plan.Assignments.Should().HaveCount(2);
        plan.OffDutyEntries.Should().ContainSingle();
        plan.DomainEvents.Should().ContainSingle(e => e is WeeklyPlanGenerated);
        plan.DomainEvents.Count(e => e is DutyAssigned).Should().Be(2);
        plan.DomainEvents.Count(e => e is UserMarkedOffDuty).Should().Be(1);
    }

    [Fact]
    public void RecalculateForSpotChanged_WhenClosed_ShouldFail()
    {
        // Arrange
        var plan = CreatePlan();
        plan.Close();
        plan.ClearDomainEvents();

        // Act
        var result = plan.RecalculateForSpotChanged(
            [new DutyAssignment(CleaningSpotId.New(), UserId.New())],
            []);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<WeekAlreadyClosedError>();
    }

    [Fact]
    public void RecalculateForSpotChanged_WhenAssignmentsContainDuplicateSpot_ShouldFail()
    {
        // Arrange
        var plan = CreatePlan();
        plan.ClearDomainEvents();
        var duplicateSpotId = CleaningSpotId.New();

        // Act
        var result = plan.RecalculateForSpotChanged(
            [
                new DutyAssignment(duplicateSpotId, UserId.New()),
                new DutyAssignment(duplicateSpotId, UserId.New())
            ],
            []);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<AssignmentConflictError>();
    }

    [Fact]
    public void RecalculateForSpotChanged_WhenValid_ShouldIncrementRevisionAndPublishReassignmentEvents()
    {
        // Arrange
        var plan = CreatePlan();
        plan.ClearDomainEvents();
        var newAssignments = new[]
        {
            new DutyAssignment(CleaningSpotId.New(), UserId.New()),
            new DutyAssignment(CleaningSpotId.New(), UserId.New())
        };
        var newOffDuty = new[] { new OffDutyEntry(UserId.New()) };

        // Act
        var result = plan.RecalculateForSpotChanged(newAssignments, newOffDuty);

        // Assert
        result.IsSuccess.Should().BeTrue();
        plan.Revision.Value.Should().Be(2);
        plan.Assignments.Should().HaveCount(2);
        plan.DomainEvents.Should().ContainSingle(e => e is WeeklyPlanRecalculated);
        plan.DomainEvents.Count(e => e is DutyReassigned).Should().Be(2);
        plan.DomainEvents.Count(e => e is UserMarkedOffDuty).Should().Be(1);
    }

    [Fact]
    public void RebalanceForUserAssigned_WhenAddedUserIsMissing_ShouldFail()
    {
        // Arrange
        var plan = CreatePlan();
        plan.ClearDomainEvents();
        var addedUserId = UserId.New();

        // Act
        var result = plan.RebalanceForUserAssigned(
            addedUserId,
            [new DutyAssignment(CleaningSpotId.New(), UserId.New())],
            []);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidRebalanceRequestError>();
    }

    [Fact]
    public void RebalanceForUserAssigned_WhenValid_ShouldIncrementRevision()
    {
        // Arrange
        var plan = CreatePlan();
        var addedUserId = UserId.New();
        plan.ClearDomainEvents();

        // Act
        var result = plan.RebalanceForUserAssigned(
            addedUserId,
            [new DutyAssignment(CleaningSpotId.New(), addedUserId)],
            []);

        // Assert
        result.IsSuccess.Should().BeTrue();
        plan.Revision.Value.Should().Be(2);
        plan.DomainEvents.Should().ContainSingle(e => e is WeeklyPlanRecalculated);
        plan.DomainEvents.Should().ContainSingle(e => e is DutyReassigned);
    }

    [Fact]
    public void RebalanceForUserUnassigned_WhenRemovedUserStillExistsInAssignments_ShouldFail()
    {
        // Arrange
        var plan = CreatePlan();
        var removedUserId = UserId.New();
        plan.ClearDomainEvents();

        // Act
        var result = plan.RebalanceForUserUnassigned(
            removedUserId,
            [new DutyAssignment(CleaningSpotId.New(), removedUserId)],
            []);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidRebalanceRequestError>();
    }

    [Fact]
    public void RebalanceForUserUnassigned_WhenValid_ShouldIncrementRevision()
    {
        // Arrange
        var plan = CreatePlan();
        var removedUserId = UserId.New();
        plan.ClearDomainEvents();

        // Act
        var result = plan.RebalanceForUserUnassigned(
            removedUserId,
            [new DutyAssignment(CleaningSpotId.New(), UserId.New())],
            []);

        // Assert
        result.IsSuccess.Should().BeTrue();
        plan.Revision.Value.Should().Be(2);
        plan.DomainEvents.Should().ContainSingle(e => e is WeeklyPlanRecalculated);
        plan.DomainEvents.Should().ContainSingle(e => e is DutyReassigned);
    }

    [Fact]
    public void Publish_WhenClosed_ShouldFail()
    {
        // Arrange
        var plan = CreatePlan();
        plan.Close();
        plan.ClearDomainEvents();

        // Act
        var result = plan.Publish();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<WeekAlreadyClosedError>();
    }

    [Fact]
    public void Publish_WhenOpen_ShouldSetPublishedAndEmitEvent()
    {
        // Arrange
        var plan = CreatePlan();
        plan.ClearDomainEvents();

        // Act
        var result = plan.Publish();

        // Assert
        result.IsSuccess.Should().BeTrue();
        plan.Status.Should().Be(WeeklyPlanStatus.Published);
        plan.DomainEvents.Should().ContainSingle(e => e is WeeklyPlanPublished);
    }

    [Fact]
    public void Close_WhenOpen_ShouldSetClosedAndEmitEvent()
    {
        // Arrange
        var plan = CreatePlan();
        plan.ClearDomainEvents();

        // Act
        var result = plan.Close();

        // Assert
        result.IsSuccess.Should().BeTrue();
        plan.Status.Should().Be(WeeklyPlanStatus.Closed);
        plan.DomainEvents.Should().ContainSingle(e => e is WeeklyPlanClosed);
    }

    [Fact]
    public void Close_WhenAlreadyClosed_ShouldSucceedWithoutExtraEvents()
    {
        // Arrange
        var plan = CreatePlan();
        plan.Close();
        plan.ClearDomainEvents();

        // Act
        var result = plan.Close();

        // Assert
        result.IsSuccess.Should().BeTrue();
        plan.Status.Should().Be(WeeklyPlanStatus.Closed);
        plan.DomainEvents.Should().BeEmpty();
    }

    private static WeeklyDutyPlan CreatePlan()
    {
        var result = WeeklyDutyPlan.Generate(
            WeeklyDutyPlanId.New(),
            CleaningAreaId.New(),
            TestDataFactory.Week(),
            AssignmentPolicy.Default,
            [new DutyAssignment(CleaningSpotId.New(), UserId.New())],
            []);

        return result.Value;
    }
}

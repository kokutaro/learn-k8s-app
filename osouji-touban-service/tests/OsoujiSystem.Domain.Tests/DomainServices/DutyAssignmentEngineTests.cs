using AwesomeAssertions;
using OsoujiSystem.Domain.DomainServices;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Tests.DomainServices;

public sealed class DutyAssignmentEngineTests
{
    [Fact]
    public void Compute_WhenSpotIsEmpty_ShouldFail()
    {
        // Arrange
        var members = new[]
        {
            new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"))
        };
        var sut = new DutyAssignmentEngine(new FairnessPolicy());

        // Act
        var result = sut.Compute([], members, RotationCursor.Start, new Dictionary<UserId, AssignmentHistorySnapshot>());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<InvalidRebalanceRequestError>();
    }

    [Fact]
    public void Compute_WhenMemberIsEmpty_ShouldFailWithFirstSpot()
    {
        // Arrange
        var spot = new CleaningSpot(CleaningSpotId.New(), "A", 1);
        var sut = new DutyAssignmentEngine(new FairnessPolicy());

        // Act
        var result = sut.Compute([spot], [], RotationCursor.Start, new Dictionary<UserId, AssignmentHistorySnapshot>());

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<NoAvailableUserForSpotError>();
        ((NoAvailableUserForSpotError)result.Error).Args["spotId"].Should().Be(spot.Id.ToString());
    }

    [Fact]
    public void Compute_WhenSpotsAreMoreThanMembers_ShouldRotateDeterministically()
    {
        // Arrange
        var user1 = UserId.New();
        var user2 = UserId.New();
        var members = new[]
        {
            new AreaMember(AreaMemberId.New(), user2, TestDataFactory.EmployeeNo("000002")),
            new AreaMember(AreaMemberId.New(), user1, TestDataFactory.EmployeeNo("000001"))
        };

        var spotA = new CleaningSpot(CleaningSpotId.New(), "A", 1);
        var spotB = new CleaningSpot(CleaningSpotId.New(), "B", 2);
        var spotC = new CleaningSpot(CleaningSpotId.New(), "C", 1);
        var spots = new[] { spotB, spotA, spotC };

        var histories = new Dictionary<UserId, AssignmentHistorySnapshot>();
        var sut = new DutyAssignmentEngine(new FairnessPolicy());
        var cursor = new RotationCursor(1);

        // Act
        var result = sut.Compute(spots, members, cursor, histories);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Assignments.Should().HaveCount(3);
        result.Value.Assignments.Select(x => x.UserId).Should().ContainInOrder(user2, user1, user2);
        result.Value.OffDutyEntries.Should().BeEmpty();
        result.Value.NextRotationCursor.Value.Should().Be(0);
    }

    [Fact]
    public void Compute_WhenMembersAreMoreThanSpots_ShouldUseFairnessAndCreateOffDutyEntries()
    {
        // Arrange
        var member1 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000003"));
        var member2 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000002"));
        var member3 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"));
        var members = new[] { member1, member2, member3 };

        var spot1 = new CleaningSpot(CleaningSpotId.New(), "A", 1);
        var spot2 = new CleaningSpot(CleaningSpotId.New(), "B", 2);

        var histories = new Dictionary<UserId, AssignmentHistorySnapshot>
        {
            [member1.UserId] = new AssignmentHistorySnapshot(member1.UserId, AssignedCountLast4Weeks: 10, ConsecutiveOffDutyWeeks: 0),
            [member2.UserId] = new AssignmentHistorySnapshot(member2.UserId, AssignedCountLast4Weeks: 3, ConsecutiveOffDutyWeeks: 2),
            [member3.UserId] = new AssignmentHistorySnapshot(member3.UserId, AssignedCountLast4Weeks: 1, ConsecutiveOffDutyWeeks: 2)
        };

        var sut = new DutyAssignmentEngine(new FairnessPolicy());

        // Act
        var result = sut.Compute([spot1, spot2], members, RotationCursor.Start, histories);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Assignments.Select(x => x.UserId).Should().ContainInOrder(member3.UserId, member2.UserId);
        result.Value.OffDutyEntries.Should().ContainSingle(x => x.UserId == member1.UserId);
        result.Value.NextRotationCursor.Value.Should().Be(1);
    }

    [Fact]
    public void RebalanceForUserAssigned_WhenSpotsGreaterThanUsersBeforeAdd_ShouldTransferOneSpot()
    {
        // Arrange
        var addedUser = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000003"));
        var donorA = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"));
        var donorB = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000002"));
        var members = new[] { donorA, donorB, addedUser };

        var spot1 = new CleaningSpot(CleaningSpotId.New(), "A", 1);
        var spot2 = new CleaningSpot(CleaningSpotId.New(), "B", 2);
        var spot3 = new CleaningSpot(CleaningSpotId.New(), "C", 3);
        var spots = new[] { spot1, spot2, spot3 };

        var histories = new Dictionary<UserId, AssignmentHistorySnapshot>
        {
            [donorA.UserId] = new AssignmentHistorySnapshot(donorA.UserId, AssignedCountLast4Weeks: 9, ConsecutiveOffDutyWeeks: 0),
            [donorB.UserId] = new AssignmentHistorySnapshot(donorB.UserId, AssignedCountLast4Weeks: 3, ConsecutiveOffDutyWeeks: 0)
        };

        var currentAssignments = new[]
        {
            new DutyAssignment(spot1.Id, donorA.UserId),
            new DutyAssignment(spot2.Id, donorA.UserId),
            new DutyAssignment(spot3.Id, donorB.UserId)
        };

        var sut = new DutyAssignmentEngine(new FairnessPolicy());

        // Act
        var result = sut.RebalanceForUserAssigned(
            new UserAssignedRebalanceInput(
                spots,
                members,
                RotationCursor.Start,
                histories,
                currentAssignments,
                [],
                addedUser.UserId,
                UsersBeforeAdd: 2));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Assignments.Count(x => x.UserId == addedUser.UserId).Should().Be(1);
        result.Value.OffDutyEntries.Should().BeEmpty();
    }

    [Fact]
    public void RebalanceForUserAssigned_WhenSpotsNotGreaterThanUsersBeforeAdd_ShouldMarkAddedUserOffDuty()
    {
        // Arrange
        var addedUser = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000003"));
        var memberA = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"));
        var memberB = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000002"));

        var spot1 = new CleaningSpot(CleaningSpotId.New(), "A", 1);
        var spot2 = new CleaningSpot(CleaningSpotId.New(), "B", 2);
        var members = new[] { memberA, memberB, addedUser };

        var currentAssignments = new[]
        {
            new DutyAssignment(spot1.Id, memberA.UserId),
            new DutyAssignment(spot2.Id, memberB.UserId)
        };

        var sut = new DutyAssignmentEngine(new FairnessPolicy());

        // Act
        var result = sut.RebalanceForUserAssigned(
            new UserAssignedRebalanceInput(
                [spot1, spot2],
                members,
                RotationCursor.Start,
                new Dictionary<UserId, AssignmentHistorySnapshot>(),
                currentAssignments,
                [],
                addedUser.UserId,
                UsersBeforeAdd: 2));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Assignments.Should().BeEquivalentTo(currentAssignments);
        result.Value.OffDutyEntries.Should().ContainSingle(x => x.UserId == addedUser.UserId);
    }

    [Fact]
    public void RebalanceForUserUnassigned_WhenRemovedUserHasAssignments_ShouldFillFromOffDutyFirst()
    {
        // Arrange
        var memberA = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"));
        var memberB = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000002"));
        var memberC = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000003"));

        var spot1 = new CleaningSpot(CleaningSpotId.New(), "A", 1);
        var spot2 = new CleaningSpot(CleaningSpotId.New(), "B", 2);
        var spot3 = new CleaningSpot(CleaningSpotId.New(), "C", 3);
        var spots = new[] { spot1, spot2, spot3 };

        var currentAssignments = new[]
        {
            new DutyAssignment(spot1.Id, memberA.UserId),
            new DutyAssignment(spot2.Id, memberB.UserId),
            new DutyAssignment(spot3.Id, memberB.UserId)
        };

        var sut = new DutyAssignmentEngine(new FairnessPolicy());

        // Act
        var result = sut.RebalanceForUserUnassigned(
            new UserUnassignedRebalanceInput(
                spots,
                [memberA, memberC],
                RotationCursor.Start,
                new Dictionary<UserId, AssignmentHistorySnapshot>(),
                currentAssignments,
                [new OffDutyEntry(memberC.UserId)],
                memberB.UserId));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Assignments.Where(x => x.UserId == memberC.UserId).Should().HaveCount(1);
        result.Value.Assignments.Should().NotContain(x => x.UserId == memberB.UserId);
    }
}

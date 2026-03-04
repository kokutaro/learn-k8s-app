using AwesomeAssertions;
using OsoujiSystem.Domain.DomainServices;
using OsoujiSystem.Domain.Entities.CleaningAreas;
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
            new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("0001"))
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
            new AreaMember(AreaMemberId.New(), user2, TestDataFactory.EmployeeNo("0002")),
            new AreaMember(AreaMemberId.New(), user1, TestDataFactory.EmployeeNo("0001"))
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
        var member1 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("0003"));
        var member2 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("0002"));
        var member3 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("0001"));
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
}

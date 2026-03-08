using AwesomeAssertions;
using OsoujiSystem.Domain.DomainServices;
using OsoujiSystem.Domain.Entities.CleaningAreas;

namespace OsoujiSystem.Domain.Tests.DomainServices;

public sealed class FairnessPolicyTests
{
    [Fact]
    public void SelectOnDutyMembers_ShouldOrderByConsecutiveOffDutyThenAssignedCountThenEmployeeNumber()
    {
        // Arrange
        var memberA = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000003"));
        var memberB = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000002"));
        var memberC = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"));

        var histories = new Dictionary<UserId, AssignmentHistorySnapshot>
        {
            [memberA.UserId] = new AssignmentHistorySnapshot(memberA.UserId, AssignedCountLast4Weeks: 5, ConsecutiveOffDutyWeeks: 1),
            [memberB.UserId] = new AssignmentHistorySnapshot(memberB.UserId, AssignedCountLast4Weeks: 3, ConsecutiveOffDutyWeeks: 2),
            [memberC.UserId] = new AssignmentHistorySnapshot(memberC.UserId, AssignedCountLast4Weeks: 3, ConsecutiveOffDutyWeeks: 2)
        };

        var sut = new FairnessPolicy();

        // Act
        var selected = sut.SelectOnDutyMembers([memberA, memberB, memberC], requiredOnDutyCount: 2, histories);

        // Assert
        selected.Should().HaveCount(2);
        selected[0].UserId.Should().Be(memberC.UserId);
        selected[1].UserId.Should().Be(memberB.UserId);
    }

    [Fact]
    public void SelectOnDutyMembers_WhenHistoryMissing_ShouldTreatAsZero()
    {
        // Arrange
        var memberA = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000002"));
        var memberB = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"));
        var histories = new Dictionary<UserId, AssignmentHistorySnapshot>();
        var sut = new FairnessPolicy();

        // Act
        var selected = sut.SelectOnDutyMembers([memberA, memberB], requiredOnDutyCount: 1, histories);

        // Assert
        selected.Should().ContainSingle();
        selected.Single().UserId.Should().Be(memberB.UserId);
    }

    [Fact]
    public void SelectOnDutyMembers_WhenRequiredCountIsZero_ShouldReturnEmpty()
    {
        // Arrange
        var member = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"));
        var sut = new FairnessPolicy();

        // Act
        var selected = sut.SelectOnDutyMembers([member], requiredOnDutyCount: 0, new Dictionary<UserId, AssignmentHistorySnapshot>());

        // Assert
        selected.Should().BeEmpty();
    }
}

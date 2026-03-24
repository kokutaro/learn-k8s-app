using AwesomeAssertions;
using OsoujiSystem.Domain.DomainServices;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

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

    [Fact]
    public void SelectClosestPhase_ShouldPrioritizeAssignmentOverlapThenDistance()
    {
        // Arrange
        var member1 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"));
        var member2 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000002"));
        var member3 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000003"));

        var spot1 = new CleaningSpot(CleaningSpotId.New(), "A", 1);
        var spot2 = new CleaningSpot(CleaningSpotId.New(), "B", 2);
        var spot3 = new CleaningSpot(CleaningSpotId.New(), "C", 3);

        var currentAssignments = new[]
        {
            new DutyAssignment(spot1.Id, member1.UserId),
            new DutyAssignment(spot2.Id, member2.UserId),
            new DutyAssignment(spot3.Id, member1.UserId)
        };

        var sut = new FairnessPolicy();

        // Act
        var phase = sut.SelectClosestPhase(
            [spot1, spot2, spot3],
            [member1, member2, member3],
            new RotationCursor(1),
            currentAssignments);

        // Assert
        phase.Value.Should().Be(0);
    }

    [Fact]
    public void SelectClosestPhase_WhenOverlapAndDistanceTie_ShouldUseHistoryAsTieBreaker()
    {
        // Arrange
        var member1 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"));
        var member2 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000002"));
        var member3 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000003"));
        var member4 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000004"));
        var member5 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000005"));
        var member6 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000006"));

        var spot1 = new CleaningSpot(CleaningSpotId.New(), "A", 1);
        var spot2 = new CleaningSpot(CleaningSpotId.New(), "B", 2);
        var spot3 = new CleaningSpot(CleaningSpotId.New(), "C", 3);
        var spot4 = new CleaningSpot(CleaningSpotId.New(), "D", 4);

        var currentAssignments = new[]
        {
            new DutyAssignment(spot1.Id, member2.UserId),
            new DutyAssignment(spot2.Id, member1.UserId),
            new DutyAssignment(spot3.Id, member2.UserId),
            new DutyAssignment(spot4.Id, member2.UserId)
        };

        var histories = new Dictionary<UserId, AssignmentHistorySnapshot>
        {
            [member1.UserId] = new AssignmentHistorySnapshot(member1.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 5),
            [member2.UserId] = new AssignmentHistorySnapshot(member2.UserId, AssignedCountLast4Weeks: 4, ConsecutiveOffDutyWeeks: 0),
            [member3.UserId] = new AssignmentHistorySnapshot(member3.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0),
            [member4.UserId] = new AssignmentHistorySnapshot(member4.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 5),
            [member5.UserId] = new AssignmentHistorySnapshot(member5.UserId, AssignedCountLast4Weeks: 4, ConsecutiveOffDutyWeeks: 0),
            [member6.UserId] = new AssignmentHistorySnapshot(member6.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0)
        };

        var sut = new FairnessPolicy();

        // Act
        var phase = sut.SelectClosestPhase(
            [spot1, spot2, spot3, spot4],
            [member1, member2, member3, member4, member5, member6],
            RotationCursor.Start,
            currentAssignments,
            histories);

        // Assert
        phase.Value.Should().Be(5);
    }

    [Fact]
    public void SelectClosestPhase_WhenOverlapTies_ShouldPreferSmallerDistanceEvenIfHistoryPenaltyIsWorse()
    {
        // Arrange
        var member1 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"));
        var member2 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000002"));
        var member3 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000003"));
        var member4 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000004"));
        var member5 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000005"));
        var member6 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000006"));

        var spot1 = new CleaningSpot(CleaningSpotId.New(), "A", 1);
        var spot2 = new CleaningSpot(CleaningSpotId.New(), "B", 2);
        var spot3 = new CleaningSpot(CleaningSpotId.New(), "C", 3);
        var spot4 = new CleaningSpot(CleaningSpotId.New(), "D", 4);

        // candidate=5 と candidate=4 が overlap=1 で同点になり、distance は 5(1) < 4(2)
        var currentAssignments = new[]
        {
            new DutyAssignment(spot1.Id, member6.UserId),
            new DutyAssignment(spot2.Id, member1.UserId),
            new DutyAssignment(spot3.Id, member1.UserId),
            new DutyAssignment(spot4.Id, member3.UserId)
        };

        // candidate=5 側の off-duty を重くしても、distance 優先で candidate=5 が選ばれることを確認
        var histories = new Dictionary<UserId, AssignmentHistorySnapshot>
        {
            [member1.UserId] = new AssignmentHistorySnapshot(member1.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0),
            [member2.UserId] = new AssignmentHistorySnapshot(member2.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 8),
            [member3.UserId] = new AssignmentHistorySnapshot(member3.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0),
            [member4.UserId] = new AssignmentHistorySnapshot(member4.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0),
            [member5.UserId] = new AssignmentHistorySnapshot(member5.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 8),
            [member6.UserId] = new AssignmentHistorySnapshot(member6.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0)
        };

        var sut = new FairnessPolicy();

        // Act
        var phase = sut.SelectClosestPhase(
            [spot1, spot2, spot3, spot4],
            [member1, member2, member3, member4, member5, member6],
            RotationCursor.Start,
            currentAssignments,
            histories);

        // Assert
        phase.Value.Should().Be(5);
    }

    [Fact]
    public void SelectClosestPhase_WhenOverlapAndDistanceTie_ShouldPreferLowerHistoryPenaltyOverSmallerPhase()
    {
        // Arrange
        var member1 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"));
        var member2 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000002"));
        var member3 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000003"));
        var member4 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000004"));
        var member5 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000005"));
        var member6 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000006"));

        var spot1 = new CleaningSpot(CleaningSpotId.New(), "A", 1);
        var spot2 = new CleaningSpot(CleaningSpotId.New(), "B", 2);
        var spot3 = new CleaningSpot(CleaningSpotId.New(), "C", 3);
        var spot4 = new CleaningSpot(CleaningSpotId.New(), "D", 4);

        // candidate=1 と candidate=5 が overlap=1, distance=1 で同点になる配置
        var currentAssignments = new[]
        {
            new DutyAssignment(spot1.Id, member2.UserId),
            new DutyAssignment(spot2.Id, member1.UserId),
            new DutyAssignment(spot3.Id, member1.UserId),
            new DutyAssignment(spot4.Id, member1.UserId)
        };

        // candidate=1 の off-duty(member1, member4)を重くして、candidate=5 の方を有利にする
        var histories = new Dictionary<UserId, AssignmentHistorySnapshot>
        {
            [member1.UserId] = new AssignmentHistorySnapshot(member1.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 8),
            [member2.UserId] = new AssignmentHistorySnapshot(member2.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0),
            [member3.UserId] = new AssignmentHistorySnapshot(member3.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0),
            [member4.UserId] = new AssignmentHistorySnapshot(member4.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 8),
            [member5.UserId] = new AssignmentHistorySnapshot(member5.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0),
            [member6.UserId] = new AssignmentHistorySnapshot(member6.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0)
        };

        var sut = new FairnessPolicy();

        // Act
        var phase = sut.SelectClosestPhase(
            [spot1, spot2, spot3, spot4],
            [member1, member2, member3, member4, member5, member6],
            RotationCursor.Start,
            currentAssignments,
            histories);

        // Assert
        phase.Value.Should().Be(5);
    }

    [Fact]
    public void SelectClosestPhase_WhenOverlapDistanceAndHistoryTie_ShouldPickSmallerPhase()
    {
        // Arrange
        var member1 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"));
        var member2 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000002"));
        var member3 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000003"));
        var member4 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000004"));
        var member5 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000005"));
        var member6 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000006"));

        var spot1 = new CleaningSpot(CleaningSpotId.New(), "A", 1);
        var spot2 = new CleaningSpot(CleaningSpotId.New(), "B", 2);
        var spot3 = new CleaningSpot(CleaningSpotId.New(), "C", 3);
        var spot4 = new CleaningSpot(CleaningSpotId.New(), "D", 4);

        // candidate=1 と candidate=5 を overlap=1, distance=1 で同点にする
        var currentAssignments = new[]
        {
            new DutyAssignment(spot1.Id, member2.UserId),
            new DutyAssignment(spot2.Id, member1.UserId),
            new DutyAssignment(spot3.Id, member1.UserId),
            new DutyAssignment(spot4.Id, member1.UserId)
        };

        // 全員同一履歴にして fairness penalty を同点化し、最終タイブレーク(phase小)を確認
        var histories = new Dictionary<UserId, AssignmentHistorySnapshot>
        {
            [member1.UserId] = new AssignmentHistorySnapshot(member1.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0),
            [member2.UserId] = new AssignmentHistorySnapshot(member2.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0),
            [member3.UserId] = new AssignmentHistorySnapshot(member3.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0),
            [member4.UserId] = new AssignmentHistorySnapshot(member4.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0),
            [member5.UserId] = new AssignmentHistorySnapshot(member5.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0),
            [member6.UserId] = new AssignmentHistorySnapshot(member6.UserId, AssignedCountLast4Weeks: 0, ConsecutiveOffDutyWeeks: 0)
        };

        var sut = new FairnessPolicy();

        // Act
        var phase = sut.SelectClosestPhase(
            [spot1, spot2, spot3, spot4],
            [member1, member2, member3, member4, member5, member6],
            RotationCursor.Start,
            currentAssignments,
            histories);

        // Assert
        phase.Value.Should().Be(1);
    }

    [Fact]
    public void SelectClosestPhase_WhenMembersExceedSpots_ShouldUseDistributedProjectionForOverlap()
    {
        // Arrange
        var member1 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000001"));
        var member2 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000002"));
        var member3 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000003"));
        var member4 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000004"));
        var member5 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000005"));
        var member6 = new AreaMember(AreaMemberId.New(), UserId.New(), TestDataFactory.EmployeeNo("000006"));

        var spot1 = new CleaningSpot(CleaningSpotId.New(), "A", 1);
        var spot2 = new CleaningSpot(CleaningSpotId.New(), "B", 2);
        var spot3 = new CleaningSpot(CleaningSpotId.New(), "C", 3);
        var spot4 = new CleaningSpot(CleaningSpotId.New(), "D", 4);

        var currentAssignments = new[]
        {
            new DutyAssignment(spot1.Id, member2.UserId),
            new DutyAssignment(spot2.Id, member3.UserId),
            new DutyAssignment(spot3.Id, member5.UserId),
            new DutyAssignment(spot4.Id, member6.UserId)
        };

        var sut = new FairnessPolicy();

        // Act
        var phase = sut.SelectClosestPhase(
            [spot1, spot2, spot3, spot4],
            [member1, member2, member3, member4, member5, member6],
            new RotationCursor(2),
            currentAssignments);

        // Assert
        phase.Value.Should().Be(1);
    }
}

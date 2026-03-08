using AwesomeAssertions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class RehydrateTests
{
    [Fact]
    public void CleaningArea_Rehydrate_ShouldRestoreStateWithoutDomainEvents()
    {
        var weekId = new WeekId(2026, 10);
        var currentRule = WeekRule.Create(DayOfWeek.Monday, new TimeOnly(0, 0), "Asia/Tokyo", weekId).Value;
        var pendingRule = WeekRule.Create(DayOfWeek.Monday, new TimeOnly(0, 0), "Asia/Tokyo", new WeekId(2026, 11)).Value;

        var spot = new CleaningSpot(CleaningSpotId.New(), "Kitchen", 1);
        var member = new AreaMember(AreaMemberId.New(), UserId.New(), EmployeeNumber.Create("123456").Value);

        var aggregate = CleaningArea.Rehydrate(
            CleaningAreaId.New(),
            "Main Area",
            currentRule,
            pendingRule,
            new RotationCursor(3),
            [spot],
            [member]);

        aggregate.Name.Should().Be("Main Area");
        aggregate.CurrentWeekRule.Should().Be(currentRule);
        aggregate.PendingWeekRule.Should().Be(pendingRule);
        aggregate.RotationCursor.Value.Should().Be(3);
        aggregate.Spots.Should().ContainSingle();
        aggregate.Members.Should().ContainSingle();
        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void WeeklyDutyPlan_Rehydrate_ShouldRestoreStateWithoutDomainEvents()
    {
        var areaId = CleaningAreaId.New();
        var weekId = new WeekId(2026, 10);
        var planId = WeeklyDutyPlanId.New();

        var assignment = new DutyAssignment(CleaningSpotId.New(), UserId.New());
        var offDuty = new OffDutyEntry(UserId.New());

        var aggregate = WeeklyDutyPlan.Rehydrate(
            planId,
            areaId,
            weekId,
            new PlanRevision(5),
            AssignmentPolicy.Default,
            WeeklyPlanStatus.Published,
            [assignment],
            [offDuty]);

        aggregate.Id.Should().Be(planId);
        aggregate.AreaId.Should().Be(areaId);
        aggregate.WeekId.Should().Be(weekId);
        aggregate.Revision.Value.Should().Be(5);
        aggregate.Status.Should().Be(WeeklyPlanStatus.Published);
        aggregate.Assignments.Should().ContainSingle();
        aggregate.OffDutyEntries.Should().ContainSingle();
        aggregate.DomainEvents.Should().BeEmpty();
    }
}

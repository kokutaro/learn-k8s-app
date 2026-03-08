using AwesomeAssertions;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Domain.Entities.UserManagement;
using OsoujiSystem.Domain.ValueObjects;
using OsoujiSystem.Infrastructure.Outbox;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class OutboxRoutingTests
{
    [Fact]
    public void GetRoutingKey_ShouldMapKnownEvents()
    {
        var areaId = new OsoujiSystem.Domain.Entities.CleaningAreas.CleaningAreaId(Guid.NewGuid());
        var planId = new OsoujiSystem.Domain.Entities.WeeklyDutyPlans.WeeklyDutyPlanId(Guid.NewGuid());
        var weekId = new WeekId(2026, 20);

        OutboxDomainEventDispatcher.GetRoutingKey(new WeeklyPlanGenerated(planId, areaId, weekId, new PlanRevision(1)))
            .Should().Be("weekly-plan.generated");
        OutboxDomainEventDispatcher.GetRoutingKey(new WeeklyPlanPublished(planId, weekId, new PlanRevision(1)))
            .Should().Be("weekly-plan.published");
        OutboxDomainEventDispatcher.GetRoutingKey(new CleaningSpotAdded(areaId, new OsoujiSystem.Domain.Entities.CleaningAreas.CleaningSpotId(Guid.NewGuid()), "Desk"))
            .Should().Be("cleaning-area.spot-added");
        OutboxDomainEventDispatcher.GetRoutingKey(new UserAssignedToArea(areaId, new OsoujiSystem.Domain.Entities.CleaningAreas.UserId(Guid.NewGuid())))
            .Should().Be("cleaning-area.user-assigned");
        OutboxDomainEventDispatcher.GetRoutingKey(new UserRegistered(Guid.NewGuid(), "123456", "Hanako", ManagedUserLifecycleStatus.Active, "OPS"))
            .Should().Be("user-registry.user-registered");
        OutboxDomainEventDispatcher.GetRoutingKey(new UserUpdated(Guid.NewGuid(), "123456", "Hanako", ManagedUserLifecycleStatus.Active, "OPS", ManagedUserChangeType.ProfileUpdated, ["displayName"]))
            .Should().Be("user-registry.user-updated");
    }
}

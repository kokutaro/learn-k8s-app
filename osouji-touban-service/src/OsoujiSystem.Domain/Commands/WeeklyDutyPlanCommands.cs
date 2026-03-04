using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Commands;

public sealed record GenerateWeeklyPlanCommand
{
    public required WeeklyDutyPlanId PlanId { get; init; }
    public required CleaningAreaId AreaId { get; init; }
    public required WeekId WeekId { get; init; }
    public AssignmentPolicy Policy { get; init; } = AssignmentPolicy.Default;
}

public sealed record RebalanceForUserAssignedCommand
{
    public required WeeklyDutyPlanId PlanId { get; init; }
    public required CleaningAreaId AreaId { get; init; }
    public required WeekId WeekId { get; init; }
    public required UserId AddedUserId { get; init; }
}

public sealed record RebalanceForUserUnassignedCommand
{
    public required WeeklyDutyPlanId PlanId { get; init; }
    public required CleaningAreaId AreaId { get; init; }
    public required WeekId WeekId { get; init; }
    public required UserId RemovedUserId { get; init; }
}

public sealed record RecalculateForSpotChangedCommand
{
    public required WeeklyDutyPlanId PlanId { get; init; }
    public required CleaningAreaId AreaId { get; init; }
    public required WeekId WeekId { get; init; }
}

public sealed record PublishWeeklyPlanCommand
{
    public required WeeklyDutyPlanId PlanId { get; init; }
}

public sealed record CloseWeeklyPlanCommand
{
    public required WeeklyDutyPlanId PlanId { get; init; }
}

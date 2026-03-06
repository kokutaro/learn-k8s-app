namespace OsoujiSystem.Application.Queries.WeeklyDutyPlans;

public sealed record WeeklyDutyPlanListItemReadModel(
    Guid Id,
    Guid AreaId,
    string WeekId,
    int Revision,
    string Status,
    long Version,
    DateTimeOffset CreatedAt);

public sealed record DutyAssignmentReadModel(
    Guid SpotId,
    Guid UserId);

public sealed record OffDutyEntryReadModel(
    Guid UserId);

public sealed record AssignmentPolicyReadModel(
    int FairnessWindowWeeks);

public sealed record WeeklyDutyPlanDetailReadModel(
    Guid Id,
    Guid AreaId,
    string WeekId,
    int Revision,
    string Status,
    AssignmentPolicyReadModel AssignmentPolicy,
    IReadOnlyList<DutyAssignmentReadModel> Assignments,
    IReadOnlyList<OffDutyEntryReadModel> OffDutyEntries,
    long Version);

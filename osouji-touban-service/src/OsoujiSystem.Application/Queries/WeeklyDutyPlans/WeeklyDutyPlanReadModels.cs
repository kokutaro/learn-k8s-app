namespace OsoujiSystem.Application.Queries.WeeklyDutyPlans;

public sealed record WeeklyDutyPlanListItemReadModel(
    Guid Id,
    Guid AreaId,
    string WeekId,
    int Revision,
    string Status,
    long Version,
    DateTimeOffset CreatedAt);

public sealed record WeeklyDutyPlanUserSummaryReadModel(
    Guid UserId,
    string EmployeeNumber,
    string DisplayName,
    string? DepartmentCode,
    string LifecycleStatus);

public sealed record DutyAssignmentReadModel(
    Guid SpotId,
    Guid UserId,
    WeeklyDutyPlanUserSummaryReadModel? User);

public sealed record OffDutyEntryReadModel(
    Guid UserId,
    WeeklyDutyPlanUserSummaryReadModel? User);

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

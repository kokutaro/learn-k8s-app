using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.DomainServices;

public sealed record UserAssignedRebalanceInput(
    IReadOnlyList<CleaningSpot> Spots,
    IReadOnlyList<AreaMember> Members,
    RotationCursor RotationCursor,
    IReadOnlyDictionary<UserId, AssignmentHistorySnapshot> Histories,
    IReadOnlyList<DutyAssignment> CurrentAssignments,
    IReadOnlyList<OffDutyEntry> CurrentOffDutyEntries,
    UserId AddedUserId,
    int UsersBeforeAdd);

public sealed record UserUnassignedRebalanceInput(
    IReadOnlyList<CleaningSpot> Spots,
    IReadOnlyList<AreaMember> Members,
    RotationCursor RotationCursor,
    IReadOnlyDictionary<UserId, AssignmentHistorySnapshot> Histories,
    IReadOnlyList<DutyAssignment> CurrentAssignments,
    IReadOnlyList<OffDutyEntry> CurrentOffDutyEntries,
    UserId RemovedUserId);

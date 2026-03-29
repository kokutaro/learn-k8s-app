using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.DomainServices;

public sealed record AssignmentEngineResult(
    IReadOnlyList<DutyAssignment> Assignments,
    IReadOnlyList<OffDutyEntry> OffDutyEntries,
    RotationCursor NextRotationCursor);

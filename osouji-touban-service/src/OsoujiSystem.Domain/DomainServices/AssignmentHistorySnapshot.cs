using OsoujiSystem.Domain.Entities.CleaningAreas;

namespace OsoujiSystem.Domain.DomainServices;

public readonly record struct AssignmentHistorySnapshot(
    UserId UserId,
    int AssignedCountLast4Weeks,
    int ConsecutiveOffDutyWeeks);

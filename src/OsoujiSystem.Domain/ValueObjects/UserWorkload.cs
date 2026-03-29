using OsoujiSystem.Domain.Entities.CleaningAreas;

namespace OsoujiSystem.Domain.ValueObjects;

public readonly record struct UserWorkload(
    UserId UserId,
    int AssignedSpotCountLast4Weeks,
    int ConsecutiveOffDutyWeeks);

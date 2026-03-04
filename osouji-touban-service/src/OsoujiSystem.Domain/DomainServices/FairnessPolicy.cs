using OsoujiSystem.Domain.Entities.CleaningAreas;

namespace OsoujiSystem.Domain.DomainServices;

public sealed class FairnessPolicy
{
    public IReadOnlyList<AreaMember> SelectOnDutyMembers(
        IReadOnlyList<AreaMember> members,
        int requiredOnDutyCount,
        IReadOnlyDictionary<UserId, AssignmentHistorySnapshot> histories)
    {
        var ordered = members
            .OrderByDescending(member => GetConsecutiveOffDuty(member.UserId, histories))
            .ThenBy(member => GetAssignedCount(member.UserId, histories))
            .ThenBy(member => member.EmployeeNumber)
            .ToList();

        return ordered.Take(requiredOnDutyCount).ToList();
    }

    private static int GetAssignedCount(
        UserId userId,
        IReadOnlyDictionary<UserId, AssignmentHistorySnapshot> histories)
    {
        return histories.TryGetValue(userId, out var history)
            ? history.AssignedCountLast4Weeks
            : 0;
    }

    private static int GetConsecutiveOffDuty(
        UserId userId,
        IReadOnlyDictionary<UserId, AssignmentHistorySnapshot> histories)
    {
        return histories.TryGetValue(userId, out var history)
            ? history.ConsecutiveOffDutyWeeks
            : 0;
    }
}

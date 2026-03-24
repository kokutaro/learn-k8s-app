using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

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

    public RotationCursor SelectClosestPhase(
        IReadOnlyList<CleaningSpot> orderedSpots,
        IReadOnlyList<AreaMember> orderedMembers,
        RotationCursor preferredPhase,
        IReadOnlyList<DutyAssignment> currentAssignments,
        IReadOnlyDictionary<UserId, AssignmentHistorySnapshot>? histories = null)
    {
        if (orderedMembers.Count == 0)
        {
            return preferredPhase;
        }

        histories ??= new Dictionary<UserId, AssignmentHistorySnapshot>();

        var basePhase = new RotationCursor(preferredPhase.Normalize(orderedMembers.Count));
        var currentBySpot = currentAssignments
            .GroupBy(x => x.SpotId)
            .ToDictionary(g => g.Key, g => g.First().UserId);

        var bestPhase = 0;
        var bestOverlap = int.MinValue;
        var bestDistance = int.MaxValue;
        var bestFairnessPenalty = int.MaxValue;

        for (var candidate = 0; candidate < orderedMembers.Count; candidate++)
        {
            var overlap = 0;
            if (orderedMembers.Count <= orderedSpots.Count)
            {
                for (var spotIndex = 0; spotIndex < orderedSpots.Count; spotIndex++)
                {
                    var spotId = orderedSpots[spotIndex].Id;
                    var userId = orderedMembers[(candidate + spotIndex) % orderedMembers.Count].UserId;

                    if (currentBySpot.TryGetValue(spotId, out var currentUserId) && currentUserId == userId)
                    {
                        overlap++;
                    }
                }
            }
            else
            {
                var layout = CommonRotationLayout.BuildDistributedSlotLayout(orderedSpots.Count, orderedMembers.Count);
                for (var memberIndex = 0; memberIndex < orderedMembers.Count; memberIndex++)
                {
                    var projectedPosition = CommonRotationLayout.ProjectedPosition(memberIndex, candidate, orderedMembers.Count);
                    var slot = layout[projectedPosition];
                    if (!slot.HasValue)
                    {
                        continue;
                    }

                    var spotId = orderedSpots[slot.Value].Id;
                    var userId = orderedMembers[memberIndex].UserId;
                    if (currentBySpot.TryGetValue(spotId, out var currentUserId) && currentUserId == userId)
                    {
                        overlap++;
                    }
                }
            }

            var distance = basePhase.CircularDistanceTo(new RotationCursor(candidate), orderedMembers.Count);
            var fairnessPenalty = ComputeOffDutyFairnessPenalty(
                orderedSpots,
                orderedMembers,
                candidate,
                histories);

            if (overlap > bestOverlap
                || (overlap == bestOverlap && distance < bestDistance)
                || (overlap == bestOverlap && distance == bestDistance && fairnessPenalty < bestFairnessPenalty)
                || (overlap == bestOverlap && distance == bestDistance && fairnessPenalty == bestFairnessPenalty && candidate < bestPhase))
            {
                bestPhase = candidate;
                bestOverlap = overlap;
                bestDistance = distance;
                bestFairnessPenalty = fairnessPenalty;
            }
        }

        return new RotationCursor(bestPhase);
    }

    private static int ComputeOffDutyFairnessPenalty(
        IReadOnlyList<CleaningSpot> orderedSpots,
        IReadOnlyList<AreaMember> orderedMembers,
        int phase,
        IReadOnlyDictionary<UserId, AssignmentHistorySnapshot> histories)
    {
        if (orderedMembers.Count <= orderedSpots.Count)
        {
            return 0;
        }

        var layout = CommonRotationLayout.BuildDistributedSlotLayout(orderedSpots.Count, orderedMembers.Count);
        var penalty = 0;
        for (var memberIndex = 0; memberIndex < orderedMembers.Count; memberIndex++)
        {
            var projectedPosition = CommonRotationLayout.ProjectedPosition(memberIndex, phase, orderedMembers.Count);
            var isOffDuty = !layout[projectedPosition].HasValue;
            if (!isOffDuty)
            {
                continue;
            }

            var member = orderedMembers[memberIndex];
            var consecutiveOffDuty = GetConsecutiveOffDuty(member.UserId, histories);
            var assignedCount = GetAssignedCount(member.UserId, histories);
            penalty += (consecutiveOffDuty * 100) - assignedCount;
        }

        return penalty;
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


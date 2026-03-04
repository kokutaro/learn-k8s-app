using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.DomainServices;

public sealed class DutyAssignmentEngine
{
    private readonly FairnessPolicy _fairnessPolicy;

    public DutyAssignmentEngine(FairnessPolicy fairnessPolicy)
    {
        _fairnessPolicy = fairnessPolicy;
    }

    public Result<AssignmentEngineResult, DomainError> Compute(
        IReadOnlyList<CleaningSpot> spots,
        IReadOnlyList<AreaMember> members,
        RotationCursor rotationCursor,
        IReadOnlyDictionary<UserId, AssignmentHistorySnapshot> histories)
    {
        if (spots.Count == 0)
        {
            return Result<AssignmentEngineResult, DomainError>.Failure(
                new InvalidRebalanceRequestError("At least one cleaning spot is required."));
        }

        if (members.Count == 0)
        {
            return Result<AssignmentEngineResult, DomainError>.Failure(
                new NoAvailableUserForSpotError(spots[0].Id));
        }

        var orderedSpots = spots
            .OrderBy(spot => spot.SortOrder)
            .ThenBy(spot => spot.Name, StringComparer.Ordinal)
            .ToArray();

        var orderedMembers = members
            .OrderBy(member => member.EmployeeNumber)
            .ToArray();

        var onDutyMembers = orderedMembers.AsEnumerable();
        var offDutyEntries = new List<OffDutyEntry>();

        if (orderedMembers.Length > orderedSpots.Length)
        {
            var selected = _fairnessPolicy.SelectOnDutyMembers(
                orderedMembers,
                orderedSpots.Length,
                histories);

            onDutyMembers = selected;
            var selectedUserIds = selected.Select(x => x.UserId).ToHashSet();

            foreach (var member in orderedMembers.Where(x => !selectedUserIds.Contains(x.UserId)))
            {
                offDutyEntries.Add(new OffDutyEntry(member.UserId));
            }
        }

        var onDutyArray = onDutyMembers.ToArray();
        var assignments = new List<DutyAssignment>(orderedSpots.Length);
        var memberIndex = onDutyArray.Length == 0
            ? 0
            : Math.Abs(rotationCursor.Value) % onDutyArray.Length;

        foreach (var spot in orderedSpots)
        {
            if (onDutyArray.Length == 0)
            {
                return Result<AssignmentEngineResult, DomainError>.Failure(new NoAvailableUserForSpotError(spot.Id));
            }

            var member = onDutyArray[memberIndex];
            assignments.Add(new DutyAssignment(spot.Id, member.UserId));
            memberIndex = (memberIndex + 1) % onDutyArray.Length;
        }

        var nextCursor = onDutyArray.Length == 0
            ? rotationCursor
            : new RotationCursor((rotationCursor.Value + 1) % onDutyArray.Length);

        return Result<AssignmentEngineResult, DomainError>.Success(
            new AssignmentEngineResult(assignments, offDutyEntries, nextCursor));
    }
}

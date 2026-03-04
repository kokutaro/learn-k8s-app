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

    public Result<AssignmentEngineResult, DomainError> RebalanceForUserAssigned(UserAssignedRebalanceInput input)
    {
        var commonValidation = ValidateCommonInput(input.Spots, input.Members);
        if (commonValidation.IsFailure)
        {
            return Result<AssignmentEngineResult, DomainError>.Failure(commonValidation.Error);
        }

        if (input.UsersBeforeAdd < 0)
        {
            return Result<AssignmentEngineResult, DomainError>.Failure(
                new InvalidRebalanceRequestError("UsersBeforeAdd must be zero or more."));
        }

        var addedMember = input.Members.FirstOrDefault(x => x.UserId == input.AddedUserId);
        if (addedMember is null)
        {
            return Result<AssignmentEngineResult, DomainError>.Failure(
                new InvalidRebalanceRequestError("Added user must exist in current members."));
        }

        if (input.Spots.Count > input.UsersBeforeAdd)
        {
            return TransferOneSpotToAddedUser(input);
        }

        var keptAssignments = input.CurrentAssignments
            .Where(x => x.UserId != input.AddedUserId)
            .ToList();

        var offDutyEntries = input.CurrentOffDutyEntries
            .Where(x => x.UserId != input.AddedUserId)
            .ToList();

        if (!offDutyEntries.Any(x => x.UserId == input.AddedUserId))
        {
            offDutyEntries.Add(new OffDutyEntry(input.AddedUserId));
        }

        return Result<AssignmentEngineResult, DomainError>.Success(
            new AssignmentEngineResult(keptAssignments, offDutyEntries, input.RotationCursor));
    }

    public Result<AssignmentEngineResult, DomainError> RebalanceForUserUnassigned(UserUnassignedRebalanceInput input)
    {
        var commonValidation = ValidateCommonInput(input.Spots, input.Members);
        if (commonValidation.IsFailure)
        {
            return Result<AssignmentEngineResult, DomainError>.Failure(commonValidation.Error);
        }

        var removedAssignments = input.CurrentAssignments
            .Where(x => x.UserId == input.RemovedUserId)
            .ToList();

        var nextAssignments = input.CurrentAssignments
            .Where(x => x.UserId != input.RemovedUserId)
            .ToList();

        var availableMembersByUserId = input.Members.ToDictionary(x => x.UserId, x => x);

        var nextOffDuty = input.CurrentOffDutyEntries
            .Where(x => x.UserId != input.RemovedUserId)
            .Where(x => availableMembersByUserId.ContainsKey(x.UserId))
            .ToList();

        if (removedAssignments.Count == 0)
        {
            return Result<AssignmentEngineResult, DomainError>.Success(
                new AssignmentEngineResult(nextAssignments, nextOffDuty, input.RotationCursor));
        }

        var usedOffDutyIds = new HashSet<UserId>();
        var offDutyCandidates = nextOffDuty
            .Select(x => x.UserId)
            .Distinct()
            .Select(id => availableMembersByUserId[id])
            .OrderBy(x => x.EmployeeNumber)
            .ToList();

        var rotationCandidates = input.Members
            .OrderBy(x => x.EmployeeNumber)
            .ToArray();

        if (rotationCandidates.Length == 0)
        {
            return Result<AssignmentEngineResult, DomainError>.Failure(new NoAvailableUserForSpotError(removedAssignments[0].SpotId));
        }

        var spotOrder = input.Spots.ToDictionary(x => x.Id, x => (x.SortOrder, x.Name));
        var rotationIndex = Math.Abs(input.RotationCursor.Value) % rotationCandidates.Length;

        foreach (var removedAssignment in removedAssignments.OrderBy(x => spotOrder[x.SpotId].SortOrder).ThenBy(x => spotOrder[x.SpotId].Name, StringComparer.Ordinal))
        {
            AreaMember selectedMember;
            if (offDutyCandidates.Count > 0)
            {
                selectedMember = offDutyCandidates[0];
                offDutyCandidates.RemoveAt(0);
                usedOffDutyIds.Add(selectedMember.UserId);
            }
            else
            {
                selectedMember = rotationCandidates[rotationIndex];
                rotationIndex = (rotationIndex + 1) % rotationCandidates.Length;
            }

            nextAssignments.Add(new DutyAssignment(removedAssignment.SpotId, selectedMember.UserId));
        }

        nextOffDuty = nextOffDuty
            .Where(x => !usedOffDutyIds.Contains(x.UserId))
            .ToList();

        var nextCursor = new RotationCursor(rotationIndex);
        return Result<AssignmentEngineResult, DomainError>.Success(
            new AssignmentEngineResult(nextAssignments, nextOffDuty, nextCursor));
    }

    private Result<Unit, DomainError> ValidateCommonInput(
        IReadOnlyList<CleaningSpot> spots,
        IReadOnlyList<AreaMember> members)
    {
        if (spots.Count == 0)
        {
            return Result<Unit, DomainError>.Failure(
                new InvalidRebalanceRequestError("At least one cleaning spot is required."));
        }

        if (members.Count == 0)
        {
            return Result<Unit, DomainError>.Failure(
                new NoAvailableUserForSpotError(spots[0].Id));
        }

        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    private Result<AssignmentEngineResult, DomainError> TransferOneSpotToAddedUser(UserAssignedRebalanceInput input)
    {
        var assignments = input.CurrentAssignments.ToList();
        var donorCandidates = input.Members
            .Where(x => x.UserId != input.AddedUserId)
            .Select(member => new
            {
                Member = member,
                CurrentAssignedCount = assignments.Count(x => x.UserId == member.UserId),
                AssignedCountLast4Weeks = GetAssignedCount(member.UserId, input.Histories)
            })
            .Where(x => x.CurrentAssignedCount > 0)
            .OrderByDescending(x => x.CurrentAssignedCount)
            .ThenByDescending(x => x.AssignedCountLast4Weeks)
            .ThenBy(x => x.Member.EmployeeNumber)
            .ToList();

        if (donorCandidates.Count == 0)
        {
            return Result<AssignmentEngineResult, DomainError>.Failure(
                new InvalidRebalanceRequestError("No donor assignment exists for user assignment rebalance."));
        }

        var donorUserId = donorCandidates[0].Member.UserId;
        var spotOrder = input.Spots.ToDictionary(x => x.Id, x => (x.SortOrder, x.Name));
        var transferTarget = assignments
            .Where(x => x.UserId == donorUserId)
            .OrderBy(x => spotOrder[x.SpotId].SortOrder)
            .ThenBy(x => spotOrder[x.SpotId].Name, StringComparer.Ordinal)
            .First();

        assignments.Remove(transferTarget);
        assignments.Add(new DutyAssignment(transferTarget.SpotId, input.AddedUserId));

        var offDutyEntries = input.CurrentOffDutyEntries
            .Where(x => x.UserId != input.AddedUserId)
            .ToList();

        return Result<AssignmentEngineResult, DomainError>.Success(
            new AssignmentEngineResult(assignments, offDutyEntries, input.RotationCursor));
    }

    private static int GetAssignedCount(
        UserId userId,
        IReadOnlyDictionary<UserId, AssignmentHistorySnapshot> histories)
    {
        return histories.TryGetValue(userId, out var history)
            ? history.AssignedCountLast4Weeks
            : 0;
    }
}

using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.DomainServices;

public sealed class DutyAssignmentEngine(FairnessPolicy fairnessPolicy)
{
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

        var selectedPhase = fairnessPolicy.SelectClosestPhase(
            orderedSpots,
            orderedMembers,
            rotationCursor,
            [],
            histories);
        var projection = ProjectByPhase(orderedSpots, orderedMembers, selectedPhase.Value);
        var nextCursor = selectedPhase.MoveNext(orderedMembers.Length);

        return Result<AssignmentEngineResult, DomainError>.Success(
            new AssignmentEngineResult(projection.Assignments, projection.OffDutyEntries, nextCursor));
    }

    public Result<AssignmentEngineResult, DomainError> RebalanceForUserAssigned(UserAssignedRebalanceInput input)
    {
        var commonValidation = ValidateCommonInput(input.Spots, input.Members);
        if (commonValidation.IsFailure)
        {
            return Result<AssignmentEngineResult, DomainError>.Failure(commonValidation.Error);
        }

        var addedMember = input.Members.FirstOrDefault(x => x.UserId == input.AddedUserId);
        if (addedMember is null)
        {
            return Result<AssignmentEngineResult, DomainError>.Failure(
                new InvalidRebalanceRequestError("Added user must exist in current members."));
        }

        var orderedSpots = input.Spots
            .OrderBy(spot => spot.SortOrder)
            .ThenBy(spot => spot.Name, StringComparer.Ordinal)
            .ToArray();

        var orderedMembers = input.Members
            .OrderBy(member => member.EmployeeNumber)
            .ToArray();

        var selectedPhase = fairnessPolicy.SelectClosestPhase(
            orderedSpots,
            orderedMembers,
            input.RotationCursor,
            input.CurrentAssignments,
            input.Histories);

        var projection = ProjectByPhase(orderedSpots, orderedMembers, selectedPhase.Value);
        var nextCursor = selectedPhase.MoveNext(orderedMembers.Length);

        return Result<AssignmentEngineResult, DomainError>.Success(
            new AssignmentEngineResult(projection.Assignments, projection.OffDutyEntries, nextCursor));
    }

    public Result<AssignmentEngineResult, DomainError> RebalanceForUserUnassigned(UserUnassignedRebalanceInput input)
    {
        var commonValidation = ValidateCommonInput(input.Spots, input.Members);
        if (commonValidation.IsFailure)
        {
            return Result<AssignmentEngineResult, DomainError>.Failure(commonValidation.Error);
        }

        var orderedSpots = input.Spots
            .OrderBy(spot => spot.SortOrder)
            .ThenBy(spot => spot.Name, StringComparer.Ordinal)
            .ToArray();

        var orderedMembers = input.Members
            .OrderBy(member => member.EmployeeNumber)
            .ToArray();

        var selectedPhase = fairnessPolicy.SelectClosestPhase(
            orderedSpots,
            orderedMembers,
            input.RotationCursor,
            input.CurrentAssignments,
            input.Histories);

        var projection = ProjectByPhase(orderedSpots, orderedMembers, selectedPhase.Value);
        var nextCursor = selectedPhase.MoveNext(orderedMembers.Length);
        return Result<AssignmentEngineResult, DomainError>.Success(
            new AssignmentEngineResult(projection.Assignments, projection.OffDutyEntries, nextCursor));
    }

    public Result<AssignmentEngineResult, DomainError> RecalculateForSpotChanged(
        IReadOnlyList<CleaningSpot> spots,
        IReadOnlyList<AreaMember> members,
        RotationCursor rotationCursor,
        IReadOnlyList<DutyAssignment> currentAssignments,
        IReadOnlyDictionary<UserId, AssignmentHistorySnapshot>? histories = null)
    {
        var commonValidation = ValidateCommonInput(spots, members);
        if (commonValidation.IsFailure)
        {
            return Result<AssignmentEngineResult, DomainError>.Failure(commonValidation.Error);
        }

        var orderedSpots = spots
            .OrderBy(spot => spot.SortOrder)
            .ThenBy(spot => spot.Name, StringComparer.Ordinal)
            .ToArray();

        var orderedMembers = members
            .OrderBy(member => member.EmployeeNumber)
            .ToArray();

        var selectedPhase = fairnessPolicy.SelectClosestPhase(
            orderedSpots,
            orderedMembers,
            rotationCursor,
            currentAssignments,
            histories);

        var projection = ProjectByPhase(orderedSpots, orderedMembers, selectedPhase.Value);
        var nextCursor = selectedPhase.MoveNext(orderedMembers.Length);

        return Result<AssignmentEngineResult, DomainError>.Success(
            new AssignmentEngineResult(projection.Assignments, projection.OffDutyEntries, nextCursor));
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

    private static AssignmentProjection ProjectByPhase(
        IReadOnlyList<CleaningSpot> orderedSpots,
        IReadOnlyList<AreaMember> orderedMembers,
        int phase)
    {
        if (orderedMembers.Count > orderedSpots.Count)
        {
            return ProjectByDistributedOffDutyLayout(orderedSpots, orderedMembers, phase);
        }

        var assignments = new List<DutyAssignment>(orderedSpots.Count);
        var offDutyEntries = new List<OffDutyEntry>();

        for (var index = 0; index < orderedSpots.Count; index++)
        {
            var member = orderedMembers[(phase + index) % orderedMembers.Count];
            assignments.Add(new DutyAssignment(orderedSpots[index].Id, member.UserId));
        }

        for (var index = orderedSpots.Count; index < orderedMembers.Count; index++)
        {
            var member = orderedMembers[(phase + index) % orderedMembers.Count];
            offDutyEntries.Add(new OffDutyEntry(member.UserId));
        }

        return new AssignmentProjection(assignments, offDutyEntries);
    }

    private static AssignmentProjection ProjectByDistributedOffDutyLayout(
        IReadOnlyList<CleaningSpot> orderedSpots,
        IReadOnlyList<AreaMember> orderedMembers,
        int phase)
    {
        var layout = CommonRotationLayout.BuildDistributedSlotLayout(orderedSpots.Count, orderedMembers.Count);
        var assignmentsBySpot = new DutyAssignment?[orderedSpots.Count];
        var offDutyEntries = new List<OffDutyEntry>(orderedMembers.Count - orderedSpots.Count);

        for (var memberIndex = 0; memberIndex < orderedMembers.Count; memberIndex++)
        {
            var projectedPosition = CommonRotationLayout.ProjectedPosition(memberIndex, phase, orderedMembers.Count);
            var slot = layout[projectedPosition];
            if (!slot.HasValue)
            {
                offDutyEntries.Add(new OffDutyEntry(orderedMembers[memberIndex].UserId));
                continue;
            }

            assignmentsBySpot[slot.Value] = new DutyAssignment(
                orderedSpots[slot.Value].Id,
                orderedMembers[memberIndex].UserId);
        }

        var assignments = new List<DutyAssignment>(orderedSpots.Count);
        for (var spotIndex = 0; spotIndex < orderedSpots.Count; spotIndex++)
        {
            assignments.Add(assignmentsBySpot[spotIndex]
                ?? throw new InvalidOperationException($"Spot index {spotIndex} was not assigned."));
        }

        return new AssignmentProjection(assignments, offDutyEntries);
    }

    private sealed record AssignmentProjection(
        IReadOnlyList<DutyAssignment> Assignments,
        IReadOnlyList<OffDutyEntry> OffDutyEntries);
}

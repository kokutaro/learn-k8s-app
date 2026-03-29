using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Entities.WeeklyDutyPlans;

public readonly record struct WeeklyDutyPlanId(Guid Value) : IStronglyTypedId<Guid>
{
    public static WeeklyDutyPlanId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
    public static implicit operator Guid(WeeklyDutyPlanId id) => id.Value;
    public static implicit operator WeeklyDutyPlanId(Guid value) => new(value);
}

public enum WeeklyPlanStatus
{
    Draft = 0,
    Published = 1,
    Closed = 2
}

public sealed class WeeklyDutyPlan : AggregateRoot<WeeklyDutyPlanId>
{
    private readonly List<DutyAssignment> _assignments = [];
    private readonly List<OffDutyEntry> _offDutyEntries = [];

    private WeeklyDutyPlan(
        WeeklyDutyPlanId id,
        CleaningAreaId areaId,
        WeekId weekId,
        AssignmentPolicy assignmentPolicy) : base(id)
    {
        AreaId = areaId;
        WeekId = weekId;
        AssignmentPolicy = assignmentPolicy;
        Revision = PlanRevision.Initial;
        Status = WeeklyPlanStatus.Draft;
    }

    public CleaningAreaId AreaId { get; private set; }
    public WeekId WeekId { get; private set; }
    public PlanRevision Revision { get; private set; }
    public AssignmentPolicy AssignmentPolicy { get; private set; }
    public WeeklyPlanStatus Status { get; private set; }
    public IReadOnlyList<DutyAssignment> Assignments => _assignments;
    public IReadOnlyList<OffDutyEntry> OffDutyEntries => _offDutyEntries;

    public static WeeklyDutyPlan Rehydrate(
        WeeklyDutyPlanId id,
        CleaningAreaId areaId,
        WeekId weekId,
        PlanRevision revision,
        AssignmentPolicy assignmentPolicy,
        WeeklyPlanStatus status,
        IReadOnlyList<DutyAssignment> assignments,
        IReadOnlyList<OffDutyEntry> offDutyEntries)
    {
        var plan = new WeeklyDutyPlan(id, areaId, weekId, assignmentPolicy);
        plan.ApplySnapshot(revision, status, assignments, offDutyEntries);
        plan.ClearDomainEvents();
        return plan;
    }

    public static Result<WeeklyDutyPlan, DomainError> Generate(
        WeeklyDutyPlanId planId,
        CleaningAreaId areaId,
        WeekId weekId,
        AssignmentPolicy policy,
        IReadOnlyList<DutyAssignment> assignments,
        IReadOnlyList<OffDutyEntry> offDutyEntries)
    {
        var plan = new WeeklyDutyPlan(planId, areaId, weekId, policy);

        var applyResult = plan.ReplaceAssignments(assignments, offDutyEntries);
        if (applyResult.IsFailure)
        {
            return Result<WeeklyDutyPlan, DomainError>.Failure(applyResult.Error);
        }

        plan.AddDomainEvent(new WeeklyPlanGenerated(plan.Id, plan.AreaId, plan.WeekId, plan.Revision));
        plan.EmitAssignmentEvents(initialPlan: true);
        return Result<WeeklyDutyPlan, DomainError>.Success(plan);
    }

    public Result<Unit, DomainError> RebalanceForUserAssigned(
        UserId addedUserId,
        IReadOnlyList<DutyAssignment> assignments,
        IReadOnlyList<OffDutyEntry> offDutyEntries)
    {
        var includedInAssignments = assignments.Any(x => x.UserId == addedUserId);
        var includedInOffDuty = offDutyEntries.Any(x => x.UserId == addedUserId);
        if (!includedInAssignments && !includedInOffDuty)
        {
            return Result<Unit, DomainError>.Failure(
                new InvalidRebalanceRequestError("Added user must exist in assignments or off-duty entries."));
        }

        return RecalculateInternal(assignments, offDutyEntries);
    }

    public Result<Unit, DomainError> RebalanceForUserUnassigned(
        UserId removedUserId,
        IReadOnlyList<DutyAssignment> assignments,
        IReadOnlyList<OffDutyEntry> offDutyEntries)
    {
        var stillAssigned = assignments.Any(x => x.UserId == removedUserId);
        var stillOffDuty = offDutyEntries.Any(x => x.UserId == removedUserId);
        if (stillAssigned || stillOffDuty)
        {
            return Result<Unit, DomainError>.Failure(
                new InvalidRebalanceRequestError("Removed user must not remain in assignments or off-duty entries."));
        }

        return RecalculateInternal(assignments, offDutyEntries);
    }

    public Result<Unit, DomainError> RecalculateForSpotChanged(
        IReadOnlyList<DutyAssignment> assignments,
        IReadOnlyList<OffDutyEntry> offDutyEntries)
    {
        if (assignments.Count == 0)
        {
            return Result<Unit, DomainError>.Failure(
                new InvalidRebalanceRequestError("Spot change recalculation requires at least one assignment."));
        }

        return RecalculateInternal(assignments, offDutyEntries);
    }

    public Result<Unit, DomainError> Publish()
    {
        if (Status == WeeklyPlanStatus.Closed)
        {
            return Result<Unit, DomainError>.Failure(new WeekAlreadyClosedError(Id));
        }

        Status = WeeklyPlanStatus.Published;
        AddDomainEvent(new WeeklyPlanPublished(Id, WeekId, Revision));
        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    public Result<Unit, DomainError> Close()
    {
        if (Status == WeeklyPlanStatus.Closed)
        {
            return Result<Unit, DomainError>.Success(Unit.Value);
        }

        Status = WeeklyPlanStatus.Closed;
        AddDomainEvent(new WeeklyPlanClosed(Id, WeekId, Revision));
        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    private Result<Unit, DomainError> ReplaceAssignments(
        IReadOnlyList<DutyAssignment> assignments,
        IReadOnlyList<OffDutyEntry> offDutyEntries)
    {
        var duplicateSpotId = assignments
            .GroupBy(x => x.SpotId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .FirstOrDefault();

        if (duplicateSpotId != default)
        {
            return Result<Unit, DomainError>.Failure(new AssignmentConflictError(duplicateSpotId));
        }

        _assignments.Clear();
        _offDutyEntries.Clear();
        _assignments.AddRange(assignments);
        _offDutyEntries.AddRange(offDutyEntries);
        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    private Result<Unit, DomainError> RecalculateInternal(
        IReadOnlyList<DutyAssignment> assignments,
        IReadOnlyList<OffDutyEntry> offDutyEntries)
    {
        if (Status == WeeklyPlanStatus.Closed)
        {
            return Result<Unit, DomainError>.Failure(new WeekAlreadyClosedError(Id));
        }

        var applyResult = ReplaceAssignments(assignments, offDutyEntries);
        if (applyResult.IsFailure)
        {
            return applyResult;
        }

        Revision = Revision.Next();
        AddDomainEvent(new WeeklyPlanRecalculated(Id, AreaId, WeekId, Revision));
        EmitAssignmentEvents(initialPlan: false);
        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    private void ApplySnapshot(
        PlanRevision revision,
        WeeklyPlanStatus status,
        IReadOnlyList<DutyAssignment> assignments,
        IReadOnlyList<OffDutyEntry> offDutyEntries)
    {
        Revision = revision;
        Status = status;
        _assignments.Clear();
        _offDutyEntries.Clear();
        _assignments.AddRange(assignments);
        _offDutyEntries.AddRange(offDutyEntries);
    }

    private void EmitAssignmentEvents(bool initialPlan)
    {
        foreach (var assignment in _assignments)
        {
            if (initialPlan)
            {
                AddDomainEvent(new DutyAssigned(Id, assignment.SpotId, assignment.UserId, WeekId, Revision));
            }
            else
            {
                AddDomainEvent(new DutyReassigned(Id, assignment.SpotId, assignment.UserId, WeekId, Revision));
            }
        }

        foreach (var offDuty in _offDutyEntries)
        {
            AddDomainEvent(new UserMarkedOffDuty(Id, offDuty.UserId, WeekId, Revision));
        }
    }
}

public sealed class DutyAssignment(CleaningSpotId spotId, UserId userId)
{
    public CleaningSpotId SpotId { get; } = spotId;
    public UserId UserId { get; } = userId;
}

public sealed class OffDutyEntry(UserId userId)
{
    public UserId UserId { get; } = userId;
}

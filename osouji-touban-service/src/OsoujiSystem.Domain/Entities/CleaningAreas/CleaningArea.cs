using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Entities.CleaningAreas;

public readonly record struct CleaningAreaId(Guid Value) : IStronglyTypedId<Guid>
{
    public static CleaningAreaId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
    public static implicit operator Guid(CleaningAreaId id) => id.Value;
    public static implicit operator CleaningAreaId(Guid value) => new(value);
}

public readonly record struct CleaningSpotId(Guid Value) : IStronglyTypedId<Guid>
{
    public static CleaningSpotId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
    public static implicit operator Guid(CleaningSpotId id) => id.Value;
    public static implicit operator CleaningSpotId(Guid value) => new(value);
}

public readonly record struct AreaMemberId(Guid Value) : IStronglyTypedId<Guid>
{
    public static AreaMemberId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
    public static implicit operator Guid(AreaMemberId id) => id.Value;
    public static implicit operator AreaMemberId(Guid value) => new(value);
}

public readonly record struct UserId(Guid Value) : IStronglyTypedId<Guid>
{
    public static UserId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
    public static implicit operator Guid(UserId id) => id.Value;
    public static implicit operator UserId(Guid value) => new(value);
}

public sealed class CleaningArea : AggregateRoot<CleaningAreaId>
{
    private readonly List<CleaningSpot> _spots = [];
    private readonly List<AreaMember> _members = [];

    private CleaningArea(
        CleaningAreaId id,
        string name,
        WeekRule weekRule) : base(id)
    {
        Name = name;
        CurrentWeekRule = weekRule;
        RotationCursor = RotationCursor.Start;
    }

    public string Name { get; private set; }
    public WeekRule CurrentWeekRule { get; private set; }
    public WeekRule? PendingWeekRule { get; private set; }
    public RotationCursor RotationCursor { get; private set; }
    public IReadOnlyList<CleaningSpot> Spots => _spots;
    public IReadOnlyList<AreaMember> Members => _members;

    public static CleaningArea Rehydrate(
        CleaningAreaId id,
        string name,
        WeekRule currentWeekRule,
        WeekRule? pendingWeekRule,
        RotationCursor rotationCursor,
        IReadOnlyList<CleaningSpot> spots,
        IReadOnlyList<AreaMember> members)
    {
        var area = new CleaningArea(id, name, currentWeekRule);
        area.ApplySnapshot(currentWeekRule, pendingWeekRule, rotationCursor, spots, members);
        area.ClearDomainEvents();
        return area;
    }

    public static Result<CleaningArea, DomainError> Register(
        CleaningAreaId id,
        string name,
        WeekRule weekRule,
        IReadOnlyList<CleaningSpot> initialSpots)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<CleaningArea, DomainError>.Failure(new InvalidWeekRuleError("Area name is required."));
        }

        if (initialSpots.Count == 0)
        {
            return Result<CleaningArea, DomainError>.Failure(new CleaningAreaHasNoSpotError(id));
        }

        var area = new CleaningArea(id, name.Trim(), weekRule);
        foreach (var spot in initialSpots)
        {
            if (area._spots.Any(x => x.Id == spot.Id))
            {
                return Result<CleaningArea, DomainError>.Failure(new DuplicateCleaningSpotError(id, spot.Id));
            }

            area._spots.Add(spot);
        }

        area.SortSpots();
        area.AddDomainEvent(new CleaningAreaRegistered(area.Id, area.Name, area.CurrentWeekRule));
        return Result<CleaningArea, DomainError>.Success(area);
    }

    public Result<Unit, DomainError> ScheduleWeekRuleChange(WeekRule weekRule)
    {
        if (weekRule.EffectiveFromWeek.CompareTo(CurrentWeekRule.EffectiveFromWeek) <= 0)
        {
            return Result<Unit, DomainError>.Failure(new InvalidWeekRuleError("EffectiveFromWeek must be next week or later."));
        }

        PendingWeekRule = weekRule;
        AddDomainEvent(new WeekRuleChangeScheduled(Id, weekRule));
        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    public Result<Unit, DomainError> ApplyPendingWeekRule(WeekId currentWeek)
    {
        if (PendingWeekRule is null)
        {
            return Result<Unit, DomainError>.Success(Unit.Value);
        }

        if (PendingWeekRule.Value.EffectiveFromWeek.CompareTo(currentWeek) > 0)
        {
            return Result<Unit, DomainError>.Success(Unit.Value);
        }

        CurrentWeekRule = PendingWeekRule.Value;
        PendingWeekRule = null;
        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    public Result<Unit, DomainError> AddSpot(CleaningSpot spot)
    {
        if (_spots.Any(x => x.Id == spot.Id))
        {
            return Result<Unit, DomainError>.Failure(new DuplicateCleaningSpotError(Id, spot.Id));
        }

        _spots.Add(spot);
        SortSpots();
        AddDomainEvent(new CleaningSpotAdded(Id, spot.Id, spot.Name));
        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    public Result<Unit, DomainError> RemoveSpot(CleaningSpotId spotId)
    {
        var target = _spots.FirstOrDefault(x => x.Id == spotId);
        if (target is null)
        {
            return Result<Unit, DomainError>.Success(Unit.Value);
        }

        if (_spots.Count <= 1)
        {
            return Result<Unit, DomainError>.Failure(new CleaningAreaHasNoSpotError(Id));
        }

        _spots.Remove(target);
        SortSpots();
        AddDomainEvent(new CleaningSpotRemoved(Id, spotId));
        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    public Result<Unit, DomainError> AssignUser(AreaMember member)
    {
        if (_members.Any(x => x.UserId == member.UserId))
        {
            return Result<Unit, DomainError>.Failure(new DuplicateAreaMemberError(Id, member.UserId));
        }

        _members.Add(member);
        SortMembers();
        AddDomainEvent(new UserAssignedToArea(Id, member.UserId));
        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    public Result<Unit, DomainError> UnassignUser(UserId userId, CleaningAreaId? transferToAreaId = null)
    {
        var target = _members.FirstOrDefault(x => x.UserId == userId);
        if (target is null)
        {
            return Result<Unit, DomainError>.Success(Unit.Value);
        }

        _members.Remove(target);
        SortMembers();
        AddDomainEvent(new UserUnassignedFromArea(Id, userId));

        if (transferToAreaId.HasValue)
        {
            AddDomainEvent(new UserTransferredFromArea(Id, userId, transferToAreaId.Value));
        }

        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    public void UpdateRotationCursor(RotationCursor cursor)
    {
        RotationCursor = cursor;
    }

    private void ApplySnapshot(
        WeekRule currentWeekRule,
        WeekRule? pendingWeekRule,
        RotationCursor rotationCursor,
        IReadOnlyList<CleaningSpot> spots,
        IReadOnlyList<AreaMember> members)
    {
        CurrentWeekRule = currentWeekRule;
        PendingWeekRule = pendingWeekRule;
        RotationCursor = rotationCursor;

        _spots.Clear();
        _members.Clear();
        _spots.AddRange(spots);
        _members.AddRange(members);

        SortSpots();
        SortMembers();
    }

    private void SortSpots()
    {
        _spots.Sort((x, y) =>
        {
            var sortCompare = x.SortOrder.CompareTo(y.SortOrder);
            return sortCompare != 0 ? sortCompare : string.Compare(x.Name, y.Name, StringComparison.Ordinal);
        });
    }

    private void SortMembers()
    {
        _members.Sort((x, y) => x.EmployeeNumber.CompareTo(y.EmployeeNumber));
    }
}

public sealed class CleaningSpot(CleaningSpotId id, string name, int sortOrder)
{
    public CleaningSpotId Id { get; } = id;
    public string Name { get; private set; } = name;
    public int SortOrder { get; private set; } = sortOrder;
}

public sealed class AreaMember(
    AreaMemberId id,
    UserId userId,
    EmployeeNumber employeeNumber)
{
    public AreaMemberId Id { get; } = id;
    public UserId UserId { get; } = userId;
    public EmployeeNumber EmployeeNumber { get; } = employeeNumber;
}

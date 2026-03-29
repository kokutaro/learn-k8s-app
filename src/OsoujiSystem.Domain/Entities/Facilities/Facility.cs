using System.Text.Json.Serialization;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Events;

namespace OsoujiSystem.Domain.Entities.Facilities;

public readonly record struct FacilityId(Guid Value) : IStronglyTypedId<Guid>
{
    public static FacilityId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
    public static implicit operator Guid(FacilityId id) => id.Value;
    public static implicit operator FacilityId(Guid value) => new(value);
}

public readonly record struct FacilityCode(string Value) : IStronglyTypedId<string>
{
    public static Result<FacilityCode, DomainError> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<FacilityCode, DomainError>.Failure(new InvalidFacilityCodeError(value));
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length is < 2 or > 50)
        {
            return Result<FacilityCode, DomainError>.Failure(new InvalidFacilityCodeError(value));
        }

        return normalized.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')
            ? Result<FacilityCode, DomainError>.Success(new FacilityCode(normalized))
            : Result<FacilityCode, DomainError>.Failure(new InvalidFacilityCodeError(value));
    }

    public override string ToString() => Value;
    public static implicit operator string(FacilityCode value) => value.Value;
}

public readonly record struct FacilityName(string Value)
{
    public static Result<FacilityName, DomainError> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<FacilityName, DomainError>.Failure(new InvalidFacilityNameError("Facility name is required."));
        }

        var normalized = value.Trim();
        if (normalized.Length > 200)
        {
            return Result<FacilityName, DomainError>.Failure(new InvalidFacilityNameError("Facility name must be 200 characters or fewer."));
        }

        return Result<FacilityName, DomainError>.Success(new FacilityName(normalized));
    }

    public override string ToString() => Value;
    public static implicit operator string(FacilityName value) => value.Value;
}

public readonly record struct FacilityTimeZone(string Value)
{
    public static Result<FacilityTimeZone, DomainError> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<FacilityTimeZone, DomainError>.Failure(new InvalidFacilityTimeZoneError(value));
        }

        var normalized = value.Trim();

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(normalized);
        }
        catch (TimeZoneNotFoundException)
        {
            return Result<FacilityTimeZone, DomainError>.Failure(new InvalidFacilityTimeZoneError(value));
        }
        catch (InvalidTimeZoneException)
        {
            return Result<FacilityTimeZone, DomainError>.Failure(new InvalidFacilityTimeZoneError(value));
        }

        return Result<FacilityTimeZone, DomainError>.Success(new FacilityTimeZone(normalized));
    }

    public override string ToString() => Value;
    public static implicit operator string(FacilityTimeZone value) => value.Value;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FacilityLifecycleStatus
{
    Active = 0,
    Inactive = 1
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FacilityChangeType
{
    Registered = 0,
    ProfileUpdated = 1,
    LifecycleChanged = 2
}

public sealed class Facility : AggregateRoot<FacilityId>
{
    private Facility(
        FacilityId id,
        FacilityCode code,
        FacilityName name,
        string? description,
        FacilityTimeZone timeZone,
        FacilityLifecycleStatus lifecycleStatus) : base(id)
    {
        Code = code;
        Name = name;
        Description = description;
        TimeZone = timeZone;
        LifecycleStatus = lifecycleStatus;
    }

    public FacilityCode Code { get; private set; }
    public FacilityName Name { get; private set; }
    public string? Description { get; private set; }
    public FacilityTimeZone TimeZone { get; private set; }
    public FacilityLifecycleStatus LifecycleStatus { get; private set; }

    public static Result<Facility, DomainError> Register(
        FacilityId id,
        FacilityCode code,
        FacilityName name,
        string? description,
        FacilityTimeZone timeZone)
    {
        var normalizedDescription = NormalizeDescription(description);
        if (normalizedDescription.IsFailure)
        {
            return Result<Facility, DomainError>.Failure(normalizedDescription.Error);
        }

        var facility = new Facility(
            id,
            code,
            name,
            normalizedDescription.Value,
            timeZone,
            FacilityLifecycleStatus.Active);

        facility.AddDomainEvent(new FacilityRegistered(
            facility.Id.Value,
            facility.Code.Value,
            facility.Name.Value,
            facility.Description,
            facility.TimeZone.Value,
            facility.LifecycleStatus));

        return Result<Facility, DomainError>.Success(facility);
    }

    public static Facility Rehydrate(
        FacilityId id,
        FacilityCode code,
        FacilityName name,
        string? description,
        FacilityTimeZone timeZone,
        FacilityLifecycleStatus lifecycleStatus)
    {
        var facility = new Facility(id, code, name, description, timeZone, lifecycleStatus);
        facility.ClearDomainEvents();
        return facility;
    }

    public Result<Unit, DomainError> UpdateProfile(
        FacilityName name,
        string? description,
        FacilityTimeZone timeZone)
    {
        var normalizedDescription = NormalizeDescription(description);
        if (normalizedDescription.IsFailure)
        {
            return Result<Unit, DomainError>.Failure(normalizedDescription.Error);
        }

        var changedFields = new List<string>();
        if (Name != name)
        {
            Name = name;
            changedFields.Add("name");
        }

        if (Description != normalizedDescription.Value)
        {
            Description = normalizedDescription.Value;
            changedFields.Add("description");
        }

        if (TimeZone != timeZone)
        {
            TimeZone = timeZone;
            changedFields.Add("timeZoneId");
        }

        if (changedFields.Count == 0)
        {
            return Result<Unit, DomainError>.Success(Unit.Value);
        }

        AddDomainEvent(new FacilityUpdated(
            Id.Value,
            Code.Value,
            Name.Value,
            Description,
            TimeZone.Value,
            LifecycleStatus,
            FacilityChangeType.ProfileUpdated,
            changedFields));

        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    public Result<Unit, DomainError> ChangeLifecycle(FacilityLifecycleStatus targetStatus)
    {
        if (LifecycleStatus == targetStatus)
        {
            return Result<Unit, DomainError>.Success(Unit.Value);
        }

        LifecycleStatus = targetStatus;
        AddDomainEvent(new FacilityUpdated(
            Id.Value,
            Code.Value,
            Name.Value,
            Description,
            TimeZone.Value,
            LifecycleStatus,
            FacilityChangeType.LifecycleChanged,
            ["lifecycleStatus"]));

        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    private static Result<string?, DomainError> NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Result<string?, DomainError>.Success(null);
        }

        var normalized = description.Trim();
        if (normalized.Length > 500)
        {
            return Result<string?, DomainError>.Failure(new InvalidFacilityDescriptionError("Facility description must be 500 characters or fewer."));
        }

        return Result<string?, DomainError>.Success(normalized);
    }
}

using System.Text.Json.Serialization;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.UserManagement.ValueObjects;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Entities.UserManagement;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ManagedUserLifecycleStatus
{
    PendingActivation = 0,
    Active = 1,
    Suspended = 2,
    Archived = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegistrationSource
{
    AdminPortal = 0,
    HrImport = 1,
    SelfService = 2,
    IdpProvisioning = 3
}

public sealed class ManagedUser : AggregateRoot<UserId>
{
    private readonly List<AuthIdentityLink> _authIdentityLinks = [];

    private ManagedUser(
        UserId id,
        EmployeeNumber employeeNumber,
        ManagedUserDisplayName displayName,
        ManagedUserEmailAddress? emailAddress,
        string? departmentCode,
        ManagedUserLifecycleStatus lifecycleStatus,
        RegistrationSource registrationSource) : base(id)
    {
        EmployeeNumber = employeeNumber;
        DisplayName = displayName;
        EmailAddress = emailAddress;
        DepartmentCode = departmentCode;
        LifecycleStatus = lifecycleStatus;
        RegistrationSource = registrationSource;
    }

    public EmployeeNumber EmployeeNumber { get; private set; }
    public ManagedUserDisplayName DisplayName { get; private set; }
    public ManagedUserEmailAddress? EmailAddress { get; private set; }
    public string? DepartmentCode { get; private set; }
    public ManagedUserLifecycleStatus LifecycleStatus { get; private set; }
    public RegistrationSource RegistrationSource { get; private set; }
    public IReadOnlyList<AuthIdentityLink> AuthIdentityLinks => _authIdentityLinks;

    public static Result<ManagedUser, DomainError> Register(
        UserId id,
        EmployeeNumber employeeNumber,
        ManagedUserDisplayName displayName,
        ManagedUserEmailAddress? emailAddress,
        string? departmentCode,
        RegistrationSource registrationSource)
    {
        var normalizedDepartmentCodeResult = NormalizeDepartmentCode(departmentCode);
        if (normalizedDepartmentCodeResult.IsFailure)
        {
            return Result<ManagedUser, DomainError>.Failure(normalizedDepartmentCodeResult.Error);
        }

        var user = new ManagedUser(
            id,
            employeeNumber,
            displayName,
            emailAddress,
            normalizedDepartmentCodeResult.Value,
            ManagedUserLifecycleStatus.Active,
            registrationSource);

        user.AddDomainEvent(new UserRegistered(
            user.Id.Value,
            user.EmployeeNumber.Value,
            user.DisplayName.Value,
            user.LifecycleStatus,
            user.DepartmentCode,
            user.EmailAddress?.Value));

        return Result<ManagedUser, DomainError>.Success(user);
    }

    public static ManagedUser Rehydrate(
        UserId id,
        EmployeeNumber employeeNumber,
        ManagedUserDisplayName displayName,
        ManagedUserEmailAddress? emailAddress,
        string? departmentCode,
        ManagedUserLifecycleStatus lifecycleStatus,
        RegistrationSource registrationSource,
        IReadOnlyList<AuthIdentityLink> authIdentityLinks)
    {
        var user = new ManagedUser(
            id,
            employeeNumber,
            displayName,
            emailAddress,
            departmentCode,
            lifecycleStatus,
            registrationSource);

        user._authIdentityLinks.AddRange(authIdentityLinks);
        user.ClearDomainEvents();
        return user;
    }

    public Result<Unit, DomainError> UpdateProfile(
        ManagedUserDisplayName? displayName,
        ManagedUserEmailAddress? emailAddress,
        string? departmentCode)
    {
        if (LifecycleStatus == ManagedUserLifecycleStatus.Archived)
        {
            return Result<Unit, DomainError>.Failure(new ManagedUserAlreadyArchivedError(Id));
        }

        var changedFields = new List<string>();

        if (displayName.HasValue && DisplayName != displayName.Value)
        {
            DisplayName = displayName.Value;
            changedFields.Add("displayName");
        }

        if (emailAddress.HasValue && EmailAddress != emailAddress.Value)
        {
            EmailAddress = emailAddress.Value;
            changedFields.Add("emailAddress");
        }

        var normalizedDepartmentCodeResult = NormalizeDepartmentCode(departmentCode);
        if (normalizedDepartmentCodeResult.IsFailure)
        {
            return Result<Unit, DomainError>.Failure(normalizedDepartmentCodeResult.Error);
        }

        if (normalizedDepartmentCodeResult.Value != DepartmentCode)
        {
            DepartmentCode = normalizedDepartmentCodeResult.Value;
            changedFields.Add("departmentCode");
        }

        if (changedFields.Count == 0)
        {
            return Result<Unit, DomainError>.Success(Unit.Value);
        }

        AddDomainEvent(new UserUpdated(
            Id.Value,
            EmployeeNumber.Value,
            DisplayName.Value,
            LifecycleStatus,
            DepartmentCode,
            ManagedUserChangeType.ProfileUpdated,
            changedFields,
            EmailAddress?.Value));

        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    public Result<Unit, DomainError> ChangeLifecycle(ManagedUserLifecycleStatus targetStatus)
    {
        if (LifecycleStatus == ManagedUserLifecycleStatus.Archived)
        {
            return targetStatus == ManagedUserLifecycleStatus.Archived
                ? Result<Unit, DomainError>.Success(Unit.Value)
                : Result<Unit, DomainError>.Failure(new ManagedUserAlreadyArchivedError(Id));
        }

        if (LifecycleStatus == targetStatus)
        {
            return Result<Unit, DomainError>.Success(Unit.Value);
        }

        LifecycleStatus = targetStatus;
        AddDomainEvent(new UserUpdated(
            Id.Value,
            EmployeeNumber.Value,
            DisplayName.Value,
            LifecycleStatus,
            DepartmentCode,
            ManagedUserChangeType.LifecycleChanged,
            ["lifecycleStatus"],
            EmailAddress?.Value));

        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    public Result<Unit, DomainError> LinkAuthIdentity(
        IdentityProviderKey identityProviderKey,
        IdentitySubject identitySubject,
        string? loginHint)
    {
        if (LifecycleStatus == ManagedUserLifecycleStatus.Archived)
        {
            return Result<Unit, DomainError>.Failure(new ManagedUserAlreadyArchivedError(Id));
        }

        if (_authIdentityLinks.Any(x =>
                x.IdentityProviderKey == identityProviderKey
                && x.IdentitySubject == identitySubject))
        {
            return Result<Unit, DomainError>.Success(Unit.Value);
        }

        _authIdentityLinks.Add(new AuthIdentityLink(
            identityProviderKey,
            identitySubject,
            NormalizeOptional(loginHint),
            DateTimeOffset.UtcNow,
            null));

        AddDomainEvent(new UserUpdated(
            Id.Value,
            EmployeeNumber.Value,
            DisplayName.Value,
            LifecycleStatus,
            DepartmentCode,
            ManagedUserChangeType.AuthIdentityLinked,
            ["authIdentityLinks"],
            EmailAddress?.Value));

        return Result<Unit, DomainError>.Success(Unit.Value);
    }

    private static Result<string?, DomainError> NormalizeDepartmentCode(string? departmentCode)
    {
        var normalized = NormalizeOptional(departmentCode);
        if (normalized is null)
        {
            return Result<string?, DomainError>.Success(null);
        }

        if (normalized.Length > 50)
        {
            return Result<string?, DomainError>.Failure(new InvalidDepartmentCodeError(departmentCode ?? string.Empty));
        }

        return Result<string?, DomainError>.Success(normalized);
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class AuthIdentityLink(
    IdentityProviderKey identityProviderKey,
    IdentitySubject identitySubject,
    string? loginHint,
    DateTimeOffset linkedAt,
    DateTimeOffset? lastValidatedAt)
{
    public IdentityProviderKey IdentityProviderKey { get; } = identityProviderKey;
    public IdentitySubject IdentitySubject { get; } = identitySubject;
    public string? LoginHint { get; } = loginHint;
    public DateTimeOffset LinkedAt { get; } = linkedAt;
    public DateTimeOffset? LastValidatedAt { get; } = lastValidatedAt;
}

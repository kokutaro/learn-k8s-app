using System.Text.Json.Serialization;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.UserManagement;

namespace OsoujiSystem.Domain.Events;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ManagedUserChangeType
{
    Registered = 0,
    ProfileUpdated = 1,
    LifecycleChanged = 2,
    AuthIdentityLinked = 3,
    AuthIdentityUnlinked = 4
}

public sealed record UserRegistered(
    Guid UserId,
    string EmployeeNumber,
    string DisplayName,
    ManagedUserLifecycleStatus LifecycleStatus,
    string? DepartmentCode) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record UserUpdated(
    Guid UserId,
    string EmployeeNumber,
    string DisplayName,
    ManagedUserLifecycleStatus LifecycleStatus,
    string? DepartmentCode,
    ManagedUserChangeType ChangeType,
    IReadOnlyList<string> ChangedFields) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

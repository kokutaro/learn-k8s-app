namespace OsoujiSystem.Application.Queries.Users;

public sealed record UserListItemReadModel(
    Guid Id,
    string EmployeeNumber,
    string DisplayName,
    string LifecycleStatus,
    string? DepartmentCode,
    long Version);

public sealed record UserDetailReadModel(
    Guid Id,
    string EmployeeNumber,
    string DisplayName,
    string? EmailAddress,
    string? DepartmentCode,
    string LifecycleStatus,
    long Version);

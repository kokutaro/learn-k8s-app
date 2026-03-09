namespace OsoujiSystem.Application.Queries.Users;

public sealed record UserListItemReadModel(
    Guid Id,
    string EmployeeNumber,
    string DisplayName,
    string LifecycleStatus,
    string? DepartmentCode,
    long Version);

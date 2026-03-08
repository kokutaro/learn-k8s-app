using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.UserManagement;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.Abstractions;

public sealed record UserDirectoryProjection(
    UserId UserId,
    EmployeeNumber EmployeeNumber,
    string DisplayName,
    ManagedUserLifecycleStatus LifecycleStatus,
    string? DepartmentCode,
    long AggregateVersion);

public interface IUserDirectoryProjectionRepository
{
    Task<UserDirectoryProjection?> FindByUserIdAsync(
        UserId userId,
        CancellationToken ct);

    Task UpsertAsync(
        UserDirectoryProjection projection,
        long aggregateVersion,
        Guid sourceEventId,
        CancellationToken ct);
}

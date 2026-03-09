using Cortex.Mediator.Queries;
using OsoujiSystem.Application.Queries.Abstractions;
using OsoujiSystem.Application.Queries.Shared;
using OsoujiSystem.Domain.Entities.UserManagement;

namespace OsoujiSystem.Application.Queries.Users;

public enum UserSortOrder
{
    DisplayNameAsc = 0,
    DisplayNameDesc = 1,
    EmployeeNumberAsc = 2,
    EmployeeNumberDesc = 3
}

public sealed record ListUsersQuery(
    string? Query,
    ManagedUserLifecycleStatus? Status,
    string? Cursor,
    int Limit,
    UserSortOrder Sort) : IQuery<CursorPage<UserListItemReadModel>>;

public sealed class ListUsersQueryHandler(
    IUserReadRepository repository)
    : IQueryHandler<ListUsersQuery, CursorPage<UserListItemReadModel>>
{
    public Task<CursorPage<UserListItemReadModel>> Handle(ListUsersQuery query, CancellationToken cancellationToken)
        => repository.ListAsync(query, cancellationToken);
}

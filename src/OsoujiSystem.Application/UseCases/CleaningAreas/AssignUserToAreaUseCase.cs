using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.UseCases.CleaningAreas;

public sealed record AssignUserToAreaRequest : ICommand<ApplicationResult<DomainUnit>>
{
    public required CleaningAreaId AreaId { get; init; }
    public AreaMemberId? AreaMemberId { get; init; }
    public required UserId UserId { get; init; }
    public EmployeeNumber? EmployeeNumber { get; init; }
    public AggregateVersion? ExpectedVersion { get; init; }
}

public sealed class AssignUserToAreaUseCase(
    ICleaningAreaRepository cleaningAreaRepository,
    IUserDirectoryProjectionRepository userDirectoryProjectionRepository,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher,
    IIdGenerator idGenerator)
    : ICommandHandler<AssignUserToAreaRequest, ApplicationResult<DomainUnit>>
{
    public Task<ApplicationResult<DomainUnit>> Handle(AssignUserToAreaRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                var loaded = await cleaningAreaRepository.FindByIdAsync(request.AreaId, token);
                if (loaded is null)
                {
                    return NotFoundErrors.Create<DomainUnit>("CleaningArea", "areaId", request.AreaId.ToString());
                }

                var existingAssigned = await cleaningAreaRepository.FindByUserIdAsync(request.UserId, token);
                if (existingAssigned is not null && existingAssigned.Value.Aggregate.Id != request.AreaId)
                {
                    var error = new UserAlreadyAssignedToAnotherAreaError(request.UserId, existingAssigned.Value.Aggregate.Id);
                    return ApplicationResult<DomainUnit>.FromDomainError(error);
                }

                var userDirectory = await userDirectoryProjectionRepository.FindByUserIdAsync(request.UserId, token);
                if (userDirectory is not null && userDirectory.LifecycleStatus != Domain.Entities.UserManagement.ManagedUserLifecycleStatus.Active)
                {
                    return ApplicationResult<DomainUnit>.FromDomainError(
                        new ManagedUserNotActiveError(request.UserId, userDirectory.LifecycleStatus));
                }

                var employeeNumber = userDirectory?.EmployeeNumber ?? request.EmployeeNumber;
                if (!employeeNumber.HasValue)
                {
                    return NotFoundErrors.Create<DomainUnit>("UserDirectory", "userId", request.UserId.ToString());
                }

                var areaMemberId = request.AreaMemberId ?? idGenerator.NewAreaMemberId();
                var result = loaded.Value.Aggregate.AssignUser(new AreaMember(
                    areaMemberId,
                    request.UserId,
                    employeeNumber.Value));

                if (result.IsFailure)
                {
                    return ApplicationResult<DomainUnit>.FromDomainError(result.Error);
                }

                await cleaningAreaRepository.SaveAsync(
                    loaded.Value.Aggregate,
                    request.ExpectedVersion ?? loaded.Value.Version,
                    token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, loaded.Value.Aggregate, token);
                return ApplicationResult<DomainUnit>.Success(DomainUnit.Value);
            },
            ct);
    }
}

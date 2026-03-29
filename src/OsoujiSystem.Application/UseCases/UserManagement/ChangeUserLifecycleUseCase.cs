using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.UserManagement;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.UserManagement;

public sealed record ChangeUserLifecycleRequest : ICommand<ApplicationResult<ChangeUserLifecycleResponse>>
{
    public required UserId UserId { get; init; }
    public required ManagedUserLifecycleStatus LifecycleStatus { get; init; }
    public AggregateVersion? ExpectedVersion { get; init; }
}

public sealed record ChangeUserLifecycleResponse(
    Guid UserId,
    string LifecycleStatus,
    long Version);

public sealed class ChangeUserLifecycleUseCase(
    IManagedUserRepository managedUserRepository,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher)
    : ICommandHandler<ChangeUserLifecycleRequest, ApplicationResult<ChangeUserLifecycleResponse>>
{
    public Task<ApplicationResult<ChangeUserLifecycleResponse>> Handle(ChangeUserLifecycleRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                var loaded = await managedUserRepository.FindByIdAsync(request.UserId, token);
                if (loaded is null)
                {
                    return NotFoundErrors.Create<ChangeUserLifecycleResponse>("ManagedUser", "userId", request.UserId.ToString());
                }

                var changeResult = loaded.Value.Aggregate.ChangeLifecycle(request.LifecycleStatus);
                if (changeResult.IsFailure)
                {
                    return ApplicationResult<ChangeUserLifecycleResponse>.FromDomainError(changeResult.Error);
                }

                var targetVersion = request.ExpectedVersion ?? loaded.Value.Version;
                var responseVersion = loaded.Value.Aggregate.DomainEvents.Count > 0
                    ? targetVersion.Next().Value
                    : targetVersion.Value;
                await managedUserRepository.SaveAsync(loaded.Value.Aggregate, targetVersion, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, loaded.Value.Aggregate, token);

                return ApplicationResult<ChangeUserLifecycleResponse>.Success(new ChangeUserLifecycleResponse(
                    request.UserId.Value,
                    request.LifecycleStatus.ToString(),
                    responseVersion));
            },
            ct);
    }
}

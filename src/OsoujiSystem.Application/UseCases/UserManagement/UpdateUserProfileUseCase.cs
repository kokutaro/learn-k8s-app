using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.UserManagement.ValueObjects;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.UserManagement;

public sealed record UpdateUserProfileRequest : ICommand<ApplicationResult<UpdateUserProfileResponse>>
{
    public required UserId UserId { get; init; }
    public string? DisplayName { get; init; }
    public string? EmailAddress { get; init; }
    public string? DepartmentCode { get; init; }
    public AggregateVersion? ExpectedVersion { get; init; }
}

public sealed record UpdateUserProfileResponse(
    Guid UserId,
    long Version);

public sealed class UpdateUserProfileUseCase(
    IManagedUserRepository managedUserRepository,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher)
    : ICommandHandler<UpdateUserProfileRequest, ApplicationResult<UpdateUserProfileResponse>>
{
    public Task<ApplicationResult<UpdateUserProfileResponse>> Handle(UpdateUserProfileRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                var loaded = await managedUserRepository.FindByIdAsync(request.UserId, token);
                if (loaded is null)
                {
                    return NotFoundErrors.Create<UpdateUserProfileResponse>("ManagedUser", "userId", request.UserId.ToString());
                }

                ManagedUserDisplayName? displayName = null;
                if (request.DisplayName is not null)
                {
                    var displayNameResult = ManagedUserDisplayName.Create(request.DisplayName);
                    if (displayNameResult.IsFailure)
                    {
                        return ApplicationResult<UpdateUserProfileResponse>.FromDomainError(displayNameResult.Error);
                    }

                    displayName = displayNameResult.Value;
                }

                ManagedUserEmailAddress? emailAddress = null;
                if (request.EmailAddress is not null)
                {
                    var emailAddressResult = ManagedUserEmailAddress.Create(request.EmailAddress);
                    if (emailAddressResult.IsFailure)
                    {
                        return ApplicationResult<UpdateUserProfileResponse>.FromDomainError(emailAddressResult.Error);
                    }

                    emailAddress = emailAddressResult.Value;
                }

                var updateResult = loaded.Value.Aggregate.UpdateProfile(
                    displayName,
                    emailAddress,
                    request.DepartmentCode);

                if (updateResult.IsFailure)
                {
                    return ApplicationResult<UpdateUserProfileResponse>.FromDomainError(updateResult.Error);
                }

                var targetVersion = request.ExpectedVersion ?? loaded.Value.Version;
                var responseVersion = loaded.Value.Aggregate.DomainEvents.Count > 0
                    ? targetVersion.Next().Value
                    : targetVersion.Value;
                await managedUserRepository.SaveAsync(loaded.Value.Aggregate, targetVersion, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, loaded.Value.Aggregate, token);

                return ApplicationResult<UpdateUserProfileResponse>.Success(new UpdateUserProfileResponse(
                    request.UserId.Value,
                    responseVersion));
            },
            ct);
    }
}

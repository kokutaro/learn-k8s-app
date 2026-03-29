using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.UserManagement.ValueObjects;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.UserManagement;

public sealed record LinkAuthIdentityRequest : ICommand<ApplicationResult<LinkAuthIdentityResponse>>
{
    public required UserId UserId { get; init; }
    public required string IdentityProviderKey { get; init; }
    public required string IdentitySubject { get; init; }
    public string? LoginHint { get; init; }
    public AggregateVersion? ExpectedVersion { get; init; }
}

public sealed record LinkAuthIdentityResponse(
    Guid UserId,
    long Version);

public sealed class LinkAuthIdentityUseCase(
    IManagedUserRepository managedUserRepository,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher)
    : ICommandHandler<LinkAuthIdentityRequest, ApplicationResult<LinkAuthIdentityResponse>>
{
    public Task<ApplicationResult<LinkAuthIdentityResponse>> Handle(LinkAuthIdentityRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                var loaded = await managedUserRepository.FindByIdAsync(request.UserId, token);
                if (loaded is null)
                {
                    return NotFoundErrors.Create<LinkAuthIdentityResponse>("ManagedUser", "userId", request.UserId.ToString());
                }

                var identityProviderKeyResult = IdentityProviderKey.Create(request.IdentityProviderKey);
                if (identityProviderKeyResult.IsFailure)
                {
                    return ApplicationResult<LinkAuthIdentityResponse>.FromDomainError(identityProviderKeyResult.Error);
                }

                var identitySubjectResult = IdentitySubject.Create(request.IdentitySubject);
                if (identitySubjectResult.IsFailure)
                {
                    return ApplicationResult<LinkAuthIdentityResponse>.FromDomainError(identitySubjectResult.Error);
                }

                var existingLinked = await managedUserRepository.FindByIdentityLinkAsync(
                    identityProviderKeyResult.Value,
                    identitySubjectResult.Value,
                    token);

                if (existingLinked is not null && existingLinked.Value.Aggregate.Id != request.UserId)
                {
                    return ApplicationResult<LinkAuthIdentityResponse>.FromDomainError(
                        new DuplicateAuthIdentityLinkError(
                            identityProviderKeyResult.Value.Value,
                            identitySubjectResult.Value.Value));
                }

                var linkResult = loaded.Value.Aggregate.LinkAuthIdentity(
                    identityProviderKeyResult.Value,
                    identitySubjectResult.Value,
                    request.LoginHint);

                if (linkResult.IsFailure)
                {
                    return ApplicationResult<LinkAuthIdentityResponse>.FromDomainError(linkResult.Error);
                }

                var targetVersion = request.ExpectedVersion ?? loaded.Value.Version;
                var responseVersion = loaded.Value.Aggregate.DomainEvents.Count > 0
                    ? targetVersion.Next().Value
                    : targetVersion.Value;
                await managedUserRepository.SaveAsync(loaded.Value.Aggregate, targetVersion, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, loaded.Value.Aggregate, token);

                return ApplicationResult<LinkAuthIdentityResponse>.Success(new LinkAuthIdentityResponse(
                    request.UserId.Value,
                    responseVersion));
            },
            ct);
    }
}

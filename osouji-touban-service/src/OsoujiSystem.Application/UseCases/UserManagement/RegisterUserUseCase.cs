using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.UserManagement;
using OsoujiSystem.Domain.Entities.UserManagement.ValueObjects;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.UseCases.UserManagement;

public sealed record RegisterUserRequest : ICommand<ApplicationResult<RegisterUserResponse>>
{
    public required string EmployeeNumber { get; init; }
    public required string DisplayName { get; init; }
    public string? EmailAddress { get; init; }
    public string? DepartmentCode { get; init; }
    public required RegistrationSource RegistrationSource { get; init; }
}

public sealed record RegisterUserResponse(
    Guid UserId,
    string EmployeeNumber,
    string LifecycleStatus);

public sealed class RegisterUserUseCase(
    IManagedUserRepository managedUserRepository,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher,
    IIdGenerator idGenerator)
    : ICommandHandler<RegisterUserRequest, ApplicationResult<RegisterUserResponse>>
{
    public Task<ApplicationResult<RegisterUserResponse>> Handle(RegisterUserRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                var employeeNumberResult = EmployeeNumber.Create(request.EmployeeNumber);
                if (employeeNumberResult.IsFailure)
                {
                    return ApplicationResult<RegisterUserResponse>.FromDomainError(employeeNumberResult.Error);
                }

                var displayNameResult = ManagedUserDisplayName.Create(request.DisplayName);
                if (displayNameResult.IsFailure)
                {
                    return ApplicationResult<RegisterUserResponse>.FromDomainError(displayNameResult.Error);
                }

                ManagedUserEmailAddress? emailAddress = null;
                if (!string.IsNullOrWhiteSpace(request.EmailAddress))
                {
                    var emailAddressResult = ManagedUserEmailAddress.Create(request.EmailAddress);
                    if (emailAddressResult.IsFailure)
                    {
                        return ApplicationResult<RegisterUserResponse>.FromDomainError(emailAddressResult.Error);
                    }

                    emailAddress = emailAddressResult.Value;
                }

                var existing = await managedUserRepository.FindByEmployeeNumberAsync(employeeNumberResult.Value, token);
                if (existing is not null)
                {
                    return ApplicationResult<RegisterUserResponse>.FromDomainError(
                        new DuplicateEmployeeNumberError(employeeNumberResult.Value.Value));
                }

                var userId = idGenerator.NewUserId();
                var registerResult = ManagedUser.Register(
                    userId,
                    employeeNumberResult.Value,
                    displayNameResult.Value,
                    emailAddress,
                    request.DepartmentCode,
                    request.RegistrationSource);

                if (registerResult.IsFailure)
                {
                    return ApplicationResult<RegisterUserResponse>.FromDomainError(registerResult.Error);
                }

                await managedUserRepository.AddAsync(registerResult.Value, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, registerResult.Value, token);

                return ApplicationResult<RegisterUserResponse>.Success(new RegisterUserResponse(
                    userId.Value,
                    registerResult.Value.EmployeeNumber.Value,
                    registerResult.Value.LifecycleStatus.ToString()));
            },
            ct);
    }
}

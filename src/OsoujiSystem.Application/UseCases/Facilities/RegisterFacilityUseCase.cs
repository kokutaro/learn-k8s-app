using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.Facilities;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.Facilities;

public sealed record RegisterFacilityRequest : ICommand<ApplicationResult<RegisterFacilityResponse>>
{
    public required string FacilityCode { get; init; }
    public required string FacilityName { get; init; }
    public string? Description { get; init; }
    public required string TimeZoneId { get; init; }
}

public sealed record RegisterFacilityResponse(
    FacilityId FacilityId,
    FacilityCode FacilityCode,
    FacilityLifecycleStatus LifecycleStatus);

public sealed class RegisterFacilityUseCase(
    IFacilityRepository facilityRepository,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher,
    IIdGenerator idGenerator)
    : ICommandHandler<RegisterFacilityRequest, ApplicationResult<RegisterFacilityResponse>>
{
    public Task<ApplicationResult<RegisterFacilityResponse>> Handle(RegisterFacilityRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                var codeResult = FacilityCode.Create(request.FacilityCode);
                if (codeResult.IsFailure)
                {
                    return ApplicationResult<RegisterFacilityResponse>.FromDomainError(codeResult.Error);
                }

                var nameResult = FacilityName.Create(request.FacilityName);
                if (nameResult.IsFailure)
                {
                    return ApplicationResult<RegisterFacilityResponse>.FromDomainError(nameResult.Error);
                }

                var timeZoneResult = FacilityTimeZone.Create(request.TimeZoneId);
                if (timeZoneResult.IsFailure)
                {
                    return ApplicationResult<RegisterFacilityResponse>.FromDomainError(timeZoneResult.Error);
                }

                var existing = await facilityRepository.FindByCodeAsync(codeResult.Value, token);
                if (existing is not null)
                {
                    return ApplicationResult<RegisterFacilityResponse>.FromDomainError(
                        new DuplicateFacilityCodeError(codeResult.Value.Value));
                }

                var registerResult = Facility.Register(
                    idGenerator.NewFacilityId(),
                    codeResult.Value,
                    nameResult.Value,
                    request.Description,
                    timeZoneResult.Value);

                if (registerResult.IsFailure)
                {
                    return ApplicationResult<RegisterFacilityResponse>.FromDomainError(registerResult.Error);
                }

                await facilityRepository.AddAsync(registerResult.Value, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, registerResult.Value, token);

                return ApplicationResult<RegisterFacilityResponse>.Success(
                    new RegisterFacilityResponse(
                        registerResult.Value.Id,
                        registerResult.Value.Code,
                        registerResult.Value.LifecycleStatus));
            },
            ct);
    }
}

using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.Facilities;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.Facilities;

public sealed record UpdateFacilityRequest : ICommand<ApplicationResult<UpdateFacilityResponse>>
{
    public required FacilityId FacilityId { get; init; }
    public required string FacilityName { get; init; }
    public string? Description { get; init; }
    public required string TimeZoneId { get; init; }
    public required AggregateVersion ExpectedVersion { get; init; }
}

public sealed record UpdateFacilityResponse(FacilityId FacilityId, long Version);

public sealed class UpdateFacilityUseCase(
    IFacilityRepository facilityRepository,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher)
    : ICommandHandler<UpdateFacilityRequest, ApplicationResult<UpdateFacilityResponse>>
{
    public Task<ApplicationResult<UpdateFacilityResponse>> Handle(UpdateFacilityRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                var loaded = await facilityRepository.FindByIdAsync(request.FacilityId, token);
                if (loaded is null)
                {
                    return NotFoundErrors.Create<UpdateFacilityResponse>("Facility", "facilityId", request.FacilityId.ToString());
                }

                var nameResult = FacilityName.Create(request.FacilityName);
                if (nameResult.IsFailure)
                {
                    return ApplicationResult<UpdateFacilityResponse>.FromDomainError(nameResult.Error);
                }

                var timeZoneResult = FacilityTimeZone.Create(request.TimeZoneId);
                if (timeZoneResult.IsFailure)
                {
                    return ApplicationResult<UpdateFacilityResponse>.FromDomainError(timeZoneResult.Error);
                }

                var result = loaded.Value.Aggregate.UpdateProfile(
                    nameResult.Value,
                    request.Description,
                    timeZoneResult.Value);
                if (result.IsFailure)
                {
                    return ApplicationResult<UpdateFacilityResponse>.FromDomainError(result.Error);
                }

                if (loaded.Value.Aggregate.DomainEvents.Count == 0)
                {
                    return ApplicationResult<UpdateFacilityResponse>.Success(
                        new UpdateFacilityResponse(
                            loaded.Value.Aggregate.Id,
                            loaded.Value.Version.Value));
                }

                await facilityRepository.SaveAsync(loaded.Value.Aggregate, request.ExpectedVersion, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, loaded.Value.Aggregate, token);

                return ApplicationResult<UpdateFacilityResponse>.Success(
                    new UpdateFacilityResponse(
                        loaded.Value.Aggregate.Id,
                        request.ExpectedVersion.Value + 1));
            },
            ct);
    }
}

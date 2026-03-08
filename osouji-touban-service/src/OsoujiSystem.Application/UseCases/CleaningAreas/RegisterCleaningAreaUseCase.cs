using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.Facilities;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.UseCases.CleaningAreas;

public sealed record RegisterCleaningAreaRequest : ICommand<ApplicationResult<RegisterCleaningAreaResponse>>
{
    public required FacilityId FacilityId { get; init; }
    public required CleaningAreaId AreaId { get; init; }
    public required string Name { get; init; }
    public required WeekRule InitialWeekRule { get; init; }
    public required IReadOnlyList<RegisterCleaningSpotInput> InitialSpots { get; init; }
}

public sealed record RegisterCleaningSpotInput(
    CleaningSpotId SpotId,
    string SpotName,
    int SortOrder);

public sealed record RegisterCleaningAreaResponse(CleaningAreaId AreaId);

public sealed class RegisterCleaningAreaUseCase(
    ICleaningAreaRepository cleaningAreaRepository,
    IFacilityDirectoryProjectionRepository facilityDirectoryProjectionRepository,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher)
    : ICommandHandler<RegisterCleaningAreaRequest, ApplicationResult<RegisterCleaningAreaResponse>>
{
    public Task<ApplicationResult<RegisterCleaningAreaResponse>> Handle(RegisterCleaningAreaRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                var facility = await facilityDirectoryProjectionRepository.FindByFacilityIdAsync(request.FacilityId, token);
                if (facility is null)
                {
                    return NotFoundErrors.Create<RegisterCleaningAreaResponse>("FacilityDirectory", "facilityId", request.FacilityId.ToString());
                }

                if (facility.LifecycleStatus != FacilityLifecycleStatus.Active)
                {
                    return ApplicationResult<RegisterCleaningAreaResponse>.FromDomainError(
                        new FacilityNotActiveError(request.FacilityId, facility.LifecycleStatus));
                }

                var initialSpots = request.InitialSpots
                    .Select(spot => new CleaningSpot(spot.SpotId, spot.SpotName, spot.SortOrder))
                    .ToArray();

                var createResult = CleaningArea.Register(
                    request.AreaId,
                    request.FacilityId,
                    request.Name,
                    request.InitialWeekRule,
                    initialSpots);

                if (createResult.IsFailure)
                {
                    return ApplicationResult<RegisterCleaningAreaResponse>.FromDomainError(createResult.Error);
                }

                var area = createResult.Value;
                await cleaningAreaRepository.AddAsync(area, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, area, token);

                return ApplicationResult<RegisterCleaningAreaResponse>.Success(new RegisterCleaningAreaResponse(area.Id));
            },
            ct);
    }
}

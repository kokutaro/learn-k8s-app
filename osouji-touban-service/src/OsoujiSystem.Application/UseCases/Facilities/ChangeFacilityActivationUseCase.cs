using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.Facilities;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.Facilities;

public sealed record ChangeFacilityActivationRequest : ICommand<ApplicationResult<ChangeFacilityActivationResponse>>
{
    public required FacilityId FacilityId { get; init; }
    public required FacilityLifecycleStatus LifecycleStatus { get; init; }
    public required AggregateVersion ExpectedVersion { get; init; }
}

public sealed record ChangeFacilityActivationResponse(
    FacilityId FacilityId,
    FacilityLifecycleStatus LifecycleStatus,
    long Version);

public sealed class ChangeFacilityActivationUseCase(
    IFacilityRepository facilityRepository,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher)
    : ICommandHandler<ChangeFacilityActivationRequest, ApplicationResult<ChangeFacilityActivationResponse>>
{
    public Task<ApplicationResult<ChangeFacilityActivationResponse>> Handle(ChangeFacilityActivationRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                var loaded = await facilityRepository.FindByIdAsync(request.FacilityId, token);
                if (loaded is null)
                {
                    return NotFoundErrors.Create<ChangeFacilityActivationResponse>("Facility", "facilityId", request.FacilityId.ToString());
                }

                var result = loaded.Value.Aggregate.ChangeLifecycle(request.LifecycleStatus);
                if (result.IsFailure)
                {
                    return ApplicationResult<ChangeFacilityActivationResponse>.FromDomainError(result.Error);
                }

                if (loaded.Value.Aggregate.DomainEvents.Count == 0)
                {
                    return ApplicationResult<ChangeFacilityActivationResponse>.Success(
                        new ChangeFacilityActivationResponse(
                            loaded.Value.Aggregate.Id,
                            loaded.Value.Aggregate.LifecycleStatus,
                            loaded.Value.Version.Value));
                }

                await facilityRepository.SaveAsync(loaded.Value.Aggregate, request.ExpectedVersion, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, loaded.Value.Aggregate, token);

                return ApplicationResult<ChangeFacilityActivationResponse>.Success(
                    new ChangeFacilityActivationResponse(
                        loaded.Value.Aggregate.Id,
                        loaded.Value.Aggregate.LifecycleStatus,
                        request.ExpectedVersion.Value + 1));
            },
            ct);
    }
}

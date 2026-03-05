using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.CleaningAreas;

public sealed record RemoveCleaningSpotRequest : IRequest<ApplicationResult<DomainUnit>>
{
    public required CleaningAreaId AreaId { get; init; }
    public required CleaningSpotId SpotId { get; init; }
}

public sealed class RemoveCleaningSpotUseCase : IRequestHandler<RemoveCleaningSpotRequest, ApplicationResult<DomainUnit>>
{
    private readonly ICleaningAreaRepository _cleaningAreaRepository;
    private readonly IApplicationTransaction _transaction;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public RemoveCleaningSpotUseCase(
        ICleaningAreaRepository cleaningAreaRepository,
        IApplicationTransaction transaction,
        IDomainEventDispatcher domainEventDispatcher)
    {
        _cleaningAreaRepository = cleaningAreaRepository;
        _transaction = transaction;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public Task<ApplicationResult<DomainUnit>> Handle(RemoveCleaningSpotRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            _transaction,
            async token =>
            {
                var loaded = await _cleaningAreaRepository.FindByIdAsync(request.AreaId, token);
                if (loaded is null)
                {
                    return NotFoundErrors.Create<DomainUnit>("CleaningArea", "areaId", request.AreaId.ToString());
                }

                var area = loaded.Value.Aggregate;
                var beforeEvents = area.DomainEvents.Count;
                var result = area.RemoveSpot(request.SpotId);
                if (result.IsFailure)
                {
                    return ApplicationResult<DomainUnit>.FromDomainError(result.Error);
                }

                if (area.DomainEvents.Count == beforeEvents)
                {
                    return ApplicationResult<DomainUnit>.Success(DomainUnit.Value);
                }

                await _cleaningAreaRepository.SaveAsync(area, loaded.Value.Version, token);
                await UseCaseExecution.DispatchAndClearAsync(_domainEventDispatcher, area, token);
                return ApplicationResult<DomainUnit>.Success(DomainUnit.Value);
            },
            ct);
    }
}

using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.CleaningAreas;

public sealed record AddCleaningSpotRequest : IRequest<ApplicationResult<DomainUnit>>
{
    public required CleaningAreaId AreaId { get; init; }
    public required CleaningSpotId SpotId { get; init; }
    public required string SpotName { get; init; }
    public required int SortOrder { get; init; }
}

public sealed class AddCleaningSpotUseCase : IRequestHandler<AddCleaningSpotRequest, ApplicationResult<DomainUnit>>
{
    private readonly ICleaningAreaRepository _cleaningAreaRepository;
    private readonly IApplicationTransaction _transaction;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public AddCleaningSpotUseCase(
        ICleaningAreaRepository cleaningAreaRepository,
        IApplicationTransaction transaction,
        IDomainEventDispatcher domainEventDispatcher)
    {
        _cleaningAreaRepository = cleaningAreaRepository;
        _transaction = transaction;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public Task<ApplicationResult<DomainUnit>> Handle(AddCleaningSpotRequest request, CancellationToken ct)
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

                var result = loaded.Value.Aggregate.AddSpot(
                    new CleaningSpot(request.SpotId, request.SpotName, request.SortOrder));

                if (result.IsFailure)
                {
                    return ApplicationResult<DomainUnit>.FromDomainError(result.Error);
                }

                await _cleaningAreaRepository.SaveAsync(loaded.Value.Aggregate, loaded.Value.Version, token);
                await UseCaseExecution.DispatchAndClearAsync(_domainEventDispatcher, loaded.Value.Aggregate, token);
                return ApplicationResult<DomainUnit>.Success(DomainUnit.Value);
            },
            ct);
    }
}

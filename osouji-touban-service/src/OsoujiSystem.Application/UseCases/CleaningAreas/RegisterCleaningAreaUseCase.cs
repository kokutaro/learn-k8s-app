using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.UseCases.CleaningAreas;

public sealed record RegisterCleaningAreaRequest : IRequest<ApplicationResult<RegisterCleaningAreaResponse>>
{
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

public sealed class RegisterCleaningAreaUseCase
    : IRequestHandler<RegisterCleaningAreaRequest, ApplicationResult<RegisterCleaningAreaResponse>>
{
    private readonly ICleaningAreaRepository _cleaningAreaRepository;
    private readonly IApplicationTransaction _transaction;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public RegisterCleaningAreaUseCase(
        ICleaningAreaRepository cleaningAreaRepository,
        IApplicationTransaction transaction,
        IDomainEventDispatcher domainEventDispatcher)
    {
        _cleaningAreaRepository = cleaningAreaRepository;
        _transaction = transaction;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public Task<ApplicationResult<RegisterCleaningAreaResponse>> Handle(RegisterCleaningAreaRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            _transaction,
            async token =>
            {
                var initialSpots = request.InitialSpots
                    .Select(spot => new CleaningSpot(spot.SpotId, spot.SpotName, spot.SortOrder))
                    .ToArray();

                var createResult = CleaningArea.Register(
                    request.AreaId,
                    request.Name,
                    request.InitialWeekRule,
                    initialSpots);

                if (createResult.IsFailure)
                {
                    return ApplicationResult<RegisterCleaningAreaResponse>.FromDomainError(createResult.Error);
                }

                var area = createResult.Value;
                await _cleaningAreaRepository.AddAsync(area, token);
                await UseCaseExecution.DispatchAndClearAsync(_domainEventDispatcher, area, token);

                return ApplicationResult<RegisterCleaningAreaResponse>.Success(new RegisterCleaningAreaResponse(area.Id));
            },
            ct);
    }
}

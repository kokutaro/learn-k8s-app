using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.UseCases.CleaningAreas;

public sealed record UnassignUserFromAreaRequest : IRequest<ApplicationResult<DomainUnit>>
{
    public required CleaningAreaId AreaId { get; init; }
    public required UserId UserId { get; init; }
}

public sealed class UnassignUserFromAreaUseCase(
    ICleaningAreaRepository cleaningAreaRepository,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher)
    : IRequestHandler<UnassignUserFromAreaRequest, ApplicationResult<DomainUnit>>
{
    public Task<ApplicationResult<DomainUnit>> Handle(UnassignUserFromAreaRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                var loaded = await cleaningAreaRepository.FindByIdAsync(request.AreaId, token);
                if (loaded is null)
                {
                    return NotFoundErrors.Create<DomainUnit>("CleaningArea", "areaId", request.AreaId.ToString());
                }

                var area = loaded.Value.Aggregate;
                var beforeEvents = area.DomainEvents.Count;

                var result = area.UnassignUser(request.UserId);
                if (result.IsFailure)
                {
                    return ApplicationResult<DomainUnit>.FromDomainError(result.Error);
                }

                if (area.DomainEvents.Count == beforeEvents)
                {
                    return ApplicationResult<DomainUnit>.Success(DomainUnit.Value);
                }

                await cleaningAreaRepository.SaveAsync(area, loaded.Value.Version, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, area, token);
                return ApplicationResult<DomainUnit>.Success(DomainUnit.Value);
            },
            ct);
    }
}

using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.UseCases.CleaningAreas;

public sealed record ScheduleWeekRuleChangeRequest : IRequest<ApplicationResult<DomainUnit>>
{
    public required CleaningAreaId AreaId { get; init; }
    public required WeekRule NextWeekRule { get; init; }
}

public sealed class ScheduleWeekRuleChangeUseCase(
    ICleaningAreaRepository cleaningAreaRepository,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher)
    : IRequestHandler<ScheduleWeekRuleChangeRequest, ApplicationResult<DomainUnit>>
{
    public Task<ApplicationResult<DomainUnit>> Handle(ScheduleWeekRuleChangeRequest request, CancellationToken ct)
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

                var result = loaded.Value.Aggregate.ScheduleWeekRuleChange(request.NextWeekRule);
                if (result.IsFailure)
                {
                    return ApplicationResult<DomainUnit>.FromDomainError(result.Error);
                }

                await cleaningAreaRepository.SaveAsync(loaded.Value.Aggregate, loaded.Value.Version, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, loaded.Value.Aggregate, token);
                return ApplicationResult<DomainUnit>.Success(DomainUnit.Value);
            },
            ct);
    }
}

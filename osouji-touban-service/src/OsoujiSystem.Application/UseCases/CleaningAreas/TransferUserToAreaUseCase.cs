using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.UseCases.CleaningAreas;

public sealed record TransferUserToAreaRequest : IRequest<ApplicationResult<DomainUnit>>
{
    public required CleaningAreaId FromAreaId { get; init; }
    public required CleaningAreaId ToAreaId { get; init; }
    public required UserId UserId { get; init; }
    public required AreaMemberId ToAreaMemberId { get; init; }
    public required EmployeeNumber EmployeeNumber { get; init; }
}

public sealed class TransferUserToAreaUseCase(
    ICleaningAreaRepository cleaningAreaRepository,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher)
    : IRequestHandler<TransferUserToAreaRequest, ApplicationResult<DomainUnit>>
{
    public Task<ApplicationResult<DomainUnit>> Handle(TransferUserToAreaRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                if (request.FromAreaId == request.ToAreaId)
                {
                    return ApplicationResult<DomainUnit>.Failure(
                        "InvalidTransferRequest",
                        "fromAreaId and toAreaId must differ.");
                }

                var fromLoaded = await cleaningAreaRepository.FindByIdAsync(request.FromAreaId, token);
                if (fromLoaded is null)
                {
                    return NotFoundErrors.Create<DomainUnit>("CleaningArea", "fromAreaId", request.FromAreaId.ToString());
                }

                var toLoaded = await cleaningAreaRepository.FindByIdAsync(request.ToAreaId, token);
                if (toLoaded is null)
                {
                    return NotFoundErrors.Create<DomainUnit>("CleaningArea", "toAreaId", request.ToAreaId.ToString());
                }

                var fromArea = fromLoaded.Value.Aggregate;
                var toArea = toLoaded.Value.Aggregate;

                var unassignResult = fromArea.UnassignUser(request.UserId, request.ToAreaId);
                if (unassignResult.IsFailure)
                {
                    return ApplicationResult<DomainUnit>.FromDomainError(unassignResult.Error);
                }

                var assignResult = toArea.AssignUser(new AreaMember(
                    request.ToAreaMemberId,
                    request.UserId,
                    request.EmployeeNumber));

                if (assignResult.IsFailure)
                {
                    return ApplicationResult<DomainUnit>.FromDomainError(assignResult.Error);
                }

                await cleaningAreaRepository.SaveAsync(fromArea, fromLoaded.Value.Version, token);
                await cleaningAreaRepository.SaveAsync(toArea, toLoaded.Value.Version, token);

                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, fromArea, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, toArea, token);

                return ApplicationResult<DomainUnit>.Success(DomainUnit.Value);
            },
            ct);
    }
}

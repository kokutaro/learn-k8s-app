using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.UseCases.CleaningAreas;

public sealed record AssignUserToAreaRequest : IRequest<ApplicationResult<DomainUnit>>
{
    public required CleaningAreaId AreaId { get; init; }
    public AreaMemberId? AreaMemberId { get; init; }
    public required UserId UserId { get; init; }
    public required EmployeeNumber EmployeeNumber { get; init; }
}

public sealed class AssignUserToAreaUseCase : IRequestHandler<AssignUserToAreaRequest, ApplicationResult<DomainUnit>>
{
    private readonly ICleaningAreaRepository _cleaningAreaRepository;
    private readonly IApplicationTransaction _transaction;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly IIdGenerator _idGenerator;

    public AssignUserToAreaUseCase(
        ICleaningAreaRepository cleaningAreaRepository,
        IApplicationTransaction transaction,
        IDomainEventDispatcher domainEventDispatcher,
        IIdGenerator idGenerator)
    {
        _cleaningAreaRepository = cleaningAreaRepository;
        _transaction = transaction;
        _domainEventDispatcher = domainEventDispatcher;
        _idGenerator = idGenerator;
    }

    public Task<ApplicationResult<DomainUnit>> Handle(AssignUserToAreaRequest request, CancellationToken ct)
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

                var existingAssigned = await _cleaningAreaRepository.FindByUserIdAsync(request.UserId, token);
                if (existingAssigned is not null && existingAssigned.Value.Aggregate.Id != request.AreaId)
                {
                    var error = new UserAlreadyAssignedToAnotherAreaError(request.UserId, existingAssigned.Value.Aggregate.Id);
                    return ApplicationResult<DomainUnit>.FromDomainError(error);
                }

                var areaMemberId = request.AreaMemberId ?? _idGenerator.NewAreaMemberId();
                var result = loaded.Value.Aggregate.AssignUser(new AreaMember(
                    areaMemberId,
                    request.UserId,
                    request.EmployeeNumber));

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

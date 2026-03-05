using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.UseCases.WeeklyDutyPlans;

public sealed record GenerateWeeklyPlanRequest : IRequest<ApplicationResult<GenerateWeeklyPlanResponse>>
{
    public required CleaningAreaId AreaId { get; init; }
    public required WeekId WeekId { get; init; }
    public AssignmentPolicy Policy { get; init; } = AssignmentPolicy.Default;
}

public sealed record GenerateWeeklyPlanResponse(
    WeeklyDutyPlanId PlanId,
    WeekId WeekId,
    PlanRevision Revision,
    WeeklyPlanStatus Status);

public sealed class GenerateWeeklyPlanUseCase
    : IRequestHandler<GenerateWeeklyPlanRequest, ApplicationResult<GenerateWeeklyPlanResponse>>
{
    private readonly ICleaningAreaRepository _cleaningAreaRepository;
    private readonly IWeeklyDutyPlanRepository _weeklyDutyPlanRepository;
    private readonly PlanComputationService _planComputationService;
    private readonly IIdGenerator _idGenerator;
    private readonly IApplicationTransaction _transaction;
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public GenerateWeeklyPlanUseCase(
        ICleaningAreaRepository cleaningAreaRepository,
        IWeeklyDutyPlanRepository weeklyDutyPlanRepository,
        PlanComputationService planComputationService,
        IIdGenerator idGenerator,
        IApplicationTransaction transaction,
        IDomainEventDispatcher domainEventDispatcher)
    {
        _cleaningAreaRepository = cleaningAreaRepository;
        _weeklyDutyPlanRepository = weeklyDutyPlanRepository;
        _planComputationService = planComputationService;
        _idGenerator = idGenerator;
        _transaction = transaction;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public Task<ApplicationResult<GenerateWeeklyPlanResponse>> Handle(GenerateWeeklyPlanRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            _transaction,
            async token =>
            {
                var existingPlan = await _weeklyDutyPlanRepository.FindByAreaAndWeekAsync(request.AreaId, request.WeekId, token);
                if (existingPlan is not null)
                {
                    return ApplicationResult<GenerateWeeklyPlanResponse>.Failure(
                        "WeeklyPlanAlreadyExists",
                        "A weekly duty plan already exists for this area and week.",
                        new Dictionary<string, object?>
                        {
                            ["areaId"] = request.AreaId.ToString(),
                            ["weekId"] = request.WeekId.ToString(),
                            ["planId"] = existingPlan.Value.Aggregate.Id.ToString()
                        });
                }

                var areaLoaded = await _cleaningAreaRepository.FindByIdAsync(request.AreaId, token);
                if (areaLoaded is null)
                {
                    return NotFoundErrors.Create<GenerateWeeklyPlanResponse>("CleaningArea", "areaId", request.AreaId.ToString());
                }

                var area = areaLoaded.Value.Aggregate;
                var computeResult = await _planComputationService.ComputeInitialAsync(area, request.WeekId, request.Policy, token);
                if (computeResult.IsFailure)
                {
                    return ApplicationResult<GenerateWeeklyPlanResponse>.FromDomainError(computeResult.Error);
                }

                var planId = _idGenerator.NewWeeklyDutyPlanId();
                var generatedResult = WeeklyDutyPlan.Generate(
                    planId,
                    area.Id,
                    request.WeekId,
                    request.Policy,
                    computeResult.Value.Assignments,
                    computeResult.Value.OffDutyEntries);

                if (generatedResult.IsFailure)
                {
                    return ApplicationResult<GenerateWeeklyPlanResponse>.FromDomainError(generatedResult.Error);
                }

                var plan = generatedResult.Value;
                area.UpdateRotationCursor(computeResult.Value.NextRotationCursor);

                await _weeklyDutyPlanRepository.AddAsync(plan, token);
                await _cleaningAreaRepository.SaveAsync(area, areaLoaded.Value.Version, token);

                await UseCaseExecution.DispatchAndClearAsync(_domainEventDispatcher, area, token);
                await UseCaseExecution.DispatchAndClearAsync(_domainEventDispatcher, plan, token);

                return ApplicationResult<GenerateWeeklyPlanResponse>.Success(
                    new GenerateWeeklyPlanResponse(plan.Id, plan.WeekId, plan.Revision, plan.Status));
            },
            ct);
    }
}

using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.UseCases.Shared;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.UseCases.WeeklyDutyPlans;

public sealed record GenerateWeeklyPlanRequest : ICommand<ApplicationResult<GenerateWeeklyPlanResponse>>
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

public sealed class GenerateWeeklyPlanUseCase(
    ICleaningAreaRepository cleaningAreaRepository,
    IWeeklyDutyPlanRepository weeklyDutyPlanRepository,
    PlanComputationService planComputationService,
    IIdGenerator idGenerator,
    IApplicationTransaction transaction,
    IDomainEventDispatcher domainEventDispatcher)
    : ICommandHandler<GenerateWeeklyPlanRequest, ApplicationResult<GenerateWeeklyPlanResponse>>
{
    public Task<ApplicationResult<GenerateWeeklyPlanResponse>> Handle(GenerateWeeklyPlanRequest request, CancellationToken ct)
    {
        return UseCaseExecution.InTransaction(
            transaction,
            async token =>
            {
                var existingPlan = await weeklyDutyPlanRepository.FindByAreaAndWeekAsync(request.AreaId, request.WeekId, token);
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

                var areaLoaded = await cleaningAreaRepository.FindByIdAsync(request.AreaId, token);
                if (areaLoaded is null)
                {
                    return NotFoundErrors.Create<GenerateWeeklyPlanResponse>("CleaningArea", "areaId", request.AreaId.ToString());
                }

                var area = areaLoaded.Value.Aggregate;
                var computeResult = await planComputationService.ComputeInitialAsync(area, request.WeekId, request.Policy, token);
                if (computeResult.IsFailure)
                {
                    return ApplicationResult<GenerateWeeklyPlanResponse>.FromDomainError(computeResult.Error);
                }

                var planId = idGenerator.NewWeeklyDutyPlanId();
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

                await weeklyDutyPlanRepository.AddAsync(plan, token);
                await cleaningAreaRepository.SaveAsync(area, areaLoaded.Value.Version, token);

                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, area, token);
                await UseCaseExecution.DispatchAndClearAsync(domainEventDispatcher, plan, token);

                return ApplicationResult<GenerateWeeklyPlanResponse>.Success(
                    new GenerateWeeklyPlanResponse(plan.Id, plan.WeekId, plan.Revision, plan.Status));
            },
            ct);
    }
}

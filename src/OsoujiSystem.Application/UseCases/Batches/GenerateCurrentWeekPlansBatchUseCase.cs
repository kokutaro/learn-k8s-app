using Cortex.Mediator;
using Cortex.Mediator.Commands;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.Time;
using OsoujiSystem.Application.UseCases.WeeklyDutyPlans;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.UseCases.Batches;

public sealed record GenerateCurrentWeekPlansBatchRequest : ICommand<ApplicationResult<GenerateCurrentWeekPlansBatchResponse>>
{
    public AssignmentPolicy Policy { get; init; } = AssignmentPolicy.Default;
}

public sealed record GenerateCurrentWeekPlansBatchResponse(
    int GeneratedCount,
    int SkippedCount,
    int FailedCount);

public sealed class GenerateCurrentWeekPlansBatchUseCase(
    ICleaningAreaRepository cleaningAreaRepository,
    IMediator mediator,
    IClock clock)
    : ICommandHandler<GenerateCurrentWeekPlansBatchRequest, ApplicationResult<GenerateCurrentWeekPlansBatchResponse>>
{
    public async Task<ApplicationResult<GenerateCurrentWeekPlansBatchResponse>> Handle(
        GenerateCurrentWeekPlansBatchRequest request,
        CancellationToken ct)
    {
        var loadedAreas = await cleaningAreaRepository.ListAllAsync(ct);

        var generated = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var loaded in loadedAreas)
        {
            var area = loaded.Aggregate;
            var currentWeek = WeekContextResolver.ResolveCurrentWeek(clock, area.CurrentWeekRule);

            var generateResult = await mediator.SendAsync(new GenerateWeeklyPlanRequest
            {
                AreaId = area.Id,
                WeekId = currentWeek,
                Policy = request.Policy
            }, ct);

            if (generateResult.IsSuccess)
            {
                generated++;
                continue;
            }

            if (generateResult.Error.Code == "WeeklyPlanAlreadyExists")
            {
                skipped++;
                continue;
            }

            failed++;
        }

        return ApplicationResult<GenerateCurrentWeekPlansBatchResponse>.Success(
            new GenerateCurrentWeekPlansBatchResponse(generated, skipped, failed));
    }
}

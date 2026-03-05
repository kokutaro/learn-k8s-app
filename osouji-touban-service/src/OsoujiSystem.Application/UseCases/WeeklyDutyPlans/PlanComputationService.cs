using OsoujiSystem.Domain.DomainServices;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Errors;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Application.UseCases.WeeklyDutyPlans;

public sealed class PlanComputationService
{
    private readonly DutyAssignmentEngine _engine;
    private readonly IAssignmentHistoryRepository _assignmentHistoryRepository;

    public PlanComputationService(
        DutyAssignmentEngine engine,
        IAssignmentHistoryRepository assignmentHistoryRepository)
    {
        _engine = engine;
        _assignmentHistoryRepository = assignmentHistoryRepository;
    }

    public async Task<OsoujiSystem.Domain.Abstractions.Result<AssignmentEngineResult, DomainError>> ComputeInitialAsync(
        CleaningArea area,
        WeekId weekId,
        AssignmentPolicy policy,
        CancellationToken ct)
    {
        var histories = await LoadHistoriesAsync(area, weekId, policy.FairnessWindowWeeks, ct);
        return _engine.Compute(area.Spots, area.Members, area.RotationCursor, histories);
    }

    public async Task<OsoujiSystem.Domain.Abstractions.Result<AssignmentEngineResult, DomainError>> RebalanceForUserAssignedAsync(
        CleaningArea area,
        WeeklyDutyPlan plan,
        UserId addedUserId,
        CancellationToken ct)
    {
        var histories = await LoadHistoriesAsync(area, plan.WeekId, plan.AssignmentPolicy.FairnessWindowWeeks, ct);

        var usersBeforeAdd = Math.Max(0, area.Members.Count - 1);
        var input = new UserAssignedRebalanceInput(
            area.Spots,
            area.Members,
            area.RotationCursor,
            histories,
            plan.Assignments,
            plan.OffDutyEntries,
            addedUserId,
            usersBeforeAdd);

        return _engine.RebalanceForUserAssigned(input);
    }

    public async Task<OsoujiSystem.Domain.Abstractions.Result<AssignmentEngineResult, DomainError>> RebalanceForUserUnassignedAsync(
        CleaningArea area,
        WeeklyDutyPlan plan,
        UserId removedUserId,
        CancellationToken ct)
    {
        var histories = await LoadHistoriesAsync(area, plan.WeekId, plan.AssignmentPolicy.FairnessWindowWeeks, ct);

        var input = new UserUnassignedRebalanceInput(
            area.Spots,
            area.Members,
            area.RotationCursor,
            histories,
            plan.Assignments,
            plan.OffDutyEntries,
            removedUserId);

        return _engine.RebalanceForUserUnassigned(input);
    }

    public async Task<OsoujiSystem.Domain.Abstractions.Result<AssignmentEngineResult, DomainError>> RecalculateForSpotChangedAsync(
        CleaningArea area,
        WeeklyDutyPlan plan,
        CancellationToken ct)
    {
        var histories = await LoadHistoriesAsync(area, plan.WeekId, plan.AssignmentPolicy.FairnessWindowWeeks, ct);
        return _engine.Compute(area.Spots, area.Members, area.RotationCursor, histories);
    }

    private Task<IReadOnlyDictionary<UserId, AssignmentHistorySnapshot>> LoadHistoriesAsync(
        CleaningArea area,
        WeekId weekId,
        int windowWeeks,
        CancellationToken ct)
    {
        return _assignmentHistoryRepository.GetSnapshotsAsync(
            area.Id,
            weekId,
            windowWeeks,
            area.Members.Select(x => x.UserId).ToArray(),
            ct);
    }
}

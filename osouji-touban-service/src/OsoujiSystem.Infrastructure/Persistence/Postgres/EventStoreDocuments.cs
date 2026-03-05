using System.Text.Json;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal static class EventStoreDocuments
{
    internal const string CleaningAreaStreamType = "cleaning_area";
    internal const string WeeklyDutyPlanStreamType = "weekly_duty_plan";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string SerializeEvent(IDomainEvent domainEvent)
        => JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), JsonOptions);

    internal static string SerializeSnapshot(CleaningArea aggregate)
    {
        var snapshot = new CleaningAreaSnapshot(
            aggregate.Name,
            aggregate.CurrentWeekRule,
            aggregate.PendingWeekRule,
            aggregate.RotationCursor.Value,
            aggregate.Spots.Select(x => new CleaningSpotSnapshot(x.Id.Value, x.Name, x.SortOrder)).ToArray(),
            aggregate.Members.Select(x => new AreaMemberSnapshot(x.Id.Value, x.UserId.Value, x.EmployeeNumber.Value)).ToArray());

        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    internal static CleaningArea DeserializeCleaningAreaSnapshot(Guid areaId, string payload)
    {
        var snapshot = JsonSerializer.Deserialize<CleaningAreaSnapshot?>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize cleaning area snapshot.");

        var spots = snapshot.Spots
            .Select(x => new CleaningSpot(new CleaningSpotId(x.Id), x.Name, x.SortOrder))
            .ToArray();

        var members = snapshot.Members
            .Select(x => new AreaMember(
                new AreaMemberId(x.Id),
                new UserId(x.UserId),
                EmployeeNumber.Create(x.EmployeeNumber).Value))
            .ToArray();

        return CleaningArea.Rehydrate(
            new CleaningAreaId(areaId),
            snapshot.Name,
            snapshot.CurrentWeekRule,
            snapshot.PendingWeekRule,
            RotationCursor.Create(snapshot.RotationCursor).Value,
            spots,
            members);
    }

    internal static string SerializeSnapshot(WeeklyDutyPlan aggregate)
    {
        var snapshot = new WeeklyDutyPlanSnapshot(
            aggregate.AreaId.Value,
            aggregate.WeekId,
            aggregate.Revision.Value,
            aggregate.AssignmentPolicy,
            aggregate.Status,
            aggregate.Assignments.Select(x => new DutyAssignmentSnapshot(x.SpotId.Value, x.UserId.Value)).ToArray(),
            aggregate.OffDutyEntries.Select(x => new OffDutyEntrySnapshot(x.UserId.Value)).ToArray());

        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    internal static WeeklyDutyPlan DeserializeWeeklyDutyPlanSnapshot(Guid planId, string payload)
    {
        var snapshot = JsonSerializer.Deserialize<WeeklyDutyPlanSnapshot?>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize weekly duty plan snapshot.");

        var assignments = snapshot.Assignments
            .Select(x => new DutyAssignment(new CleaningSpotId(x.SpotId), new UserId(x.UserId)))
            .ToArray();

        var offDutyEntries = snapshot.OffDutyEntries
            .Select(x => new OffDutyEntry(new UserId(x.UserId)))
            .ToArray();

        return WeeklyDutyPlan.Rehydrate(
            new WeeklyDutyPlanId(planId),
            new CleaningAreaId(snapshot.AreaId),
            snapshot.WeekId,
            new PlanRevision(snapshot.Revision),
            snapshot.AssignmentPolicy,
            snapshot.Status,
            assignments,
            offDutyEntries);
    }

    internal sealed record CleaningAreaSnapshot(
        string Name,
        WeekRule CurrentWeekRule,
        WeekRule? PendingWeekRule,
        int RotationCursor,
        IReadOnlyList<CleaningSpotSnapshot> Spots,
        IReadOnlyList<AreaMemberSnapshot> Members);

    internal readonly record struct CleaningSpotSnapshot(Guid Id, string Name, int SortOrder);
    internal readonly record struct AreaMemberSnapshot(Guid Id, Guid UserId, string EmployeeNumber);

    internal sealed record WeeklyDutyPlanSnapshot(
        Guid AreaId,
        WeekId WeekId,
        int Revision,
        AssignmentPolicy AssignmentPolicy,
        WeeklyPlanStatus Status,
        IReadOnlyList<DutyAssignmentSnapshot> Assignments,
        IReadOnlyList<OffDutyEntrySnapshot> OffDutyEntries);

    internal readonly record struct DutyAssignmentSnapshot(Guid SpotId, Guid UserId);
    internal readonly record struct OffDutyEntrySnapshot(Guid UserId);
}

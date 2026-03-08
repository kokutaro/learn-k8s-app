using System.Text.Json;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.UserManagement;
using OsoujiSystem.Domain.Entities.UserManagement.ValueObjects;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal static class EventStoreDocuments
{
    internal const string CleaningAreaStreamType = "cleaning_area";
    internal const string WeeklyDutyPlanStreamType = "weekly_duty_plan";
    internal const string ManagedUserStreamType = "managed_user";
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

    internal static string SerializeSnapshot(ManagedUser aggregate)
    {
        var snapshot = new ManagedUserSnapshot(
            aggregate.EmployeeNumber.Value,
            aggregate.DisplayName.Value,
            aggregate.EmailAddress?.Value,
            aggregate.DepartmentCode,
            aggregate.LifecycleStatus,
            aggregate.RegistrationSource,
            aggregate.AuthIdentityLinks.Select(x => new AuthIdentityLinkSnapshot(
                x.IdentityProviderKey.Value,
                x.IdentitySubject.Value,
                x.LoginHint,
                x.LinkedAt,
                x.LastValidatedAt)).ToArray());

        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    internal static ManagedUser DeserializeManagedUserSnapshot(Guid userId, string payload)
    {
        var snapshot = JsonSerializer.Deserialize<ManagedUserSnapshot?>(payload, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize managed user snapshot.");

        var identityLinks = snapshot.AuthIdentityLinks
            .Select(x => new AuthIdentityLink(
                IdentityProviderKey.Create(x.IdentityProviderKey).Value,
                IdentitySubject.Create(x.IdentitySubject).Value,
                x.LoginHint,
                x.LinkedAt,
                x.LastValidatedAt))
            .ToArray();

        return ManagedUser.Rehydrate(
            new UserId(userId),
            EmployeeNumber.Create(snapshot.EmployeeNumber).Value,
            ManagedUserDisplayName.Create(snapshot.DisplayName).Value,
            snapshot.EmailAddress is null ? null : ManagedUserEmailAddress.Create(snapshot.EmailAddress).Value,
            snapshot.DepartmentCode,
            snapshot.LifecycleStatus,
            snapshot.RegistrationSource,
            identityLinks);
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

    internal sealed record ManagedUserSnapshot(
        string EmployeeNumber,
        string DisplayName,
        string? EmailAddress,
        string? DepartmentCode,
        ManagedUserLifecycleStatus LifecycleStatus,
        RegistrationSource RegistrationSource,
        IReadOnlyList<AuthIdentityLinkSnapshot> AuthIdentityLinks);

    internal readonly record struct AuthIdentityLinkSnapshot(
        string IdentityProviderKey,
        string IdentitySubject,
        string? LoginHint,
        DateTimeOffset LinkedAt,
        DateTimeOffset? LastValidatedAt);
}

using System.Text;
using Microsoft.Extensions.Logging;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.Notifications;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Domain.ValueObjects;
using OsoujiSystem.Infrastructure.Serialization;

namespace OsoujiSystem.Infrastructure.Notifications;

internal sealed class WeeklyPlanNotificationFactory(
    IWeeklyDutyPlanRepository weeklyDutyPlanRepository,
    ICleaningAreaRepository cleaningAreaRepository,
    IClock clock,
    ILogger<WeeklyPlanNotificationFactory> logger,
    InfrastructureJsonSerializer jsonSerializer)
{
    public async Task<IReadOnlyList<UserNotification>> BuildAsync(
        string routingKey,
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, object?> headers,
        CancellationToken ct)
    {
        if (!TryReadEventId(headers, out var eventId))
        {
            throw new InvalidOperationException("Notification message must contain event_id header.");
        }

        if (DeserializeEvent(routingKey, body.Span) is not { } source)
        {
            return [];
        }

        var loadedPlan = await weeklyDutyPlanRepository.FindByIdAsync(source.PlanId, ct);
        if (loadedPlan is null)
        {
            throw new InvalidOperationException($"Weekly duty plan {source.PlanId} was not found for notification processing.");
        }

        var plan = loadedPlan.Value.Aggregate;
        if (plan.Status != WeeklyPlanStatus.Published)
        {
            logger.LogDebug(
                "Skipping notification because plan is not published. PlanId={PlanId}, Status={Status}",
                plan.Id,
                plan.Status);
            return [];
        }

        var loadedArea = await cleaningAreaRepository.FindByIdAsync(plan.AreaId, ct);
        if (loadedArea is null)
        {
            throw new InvalidOperationException($"Cleaning area {plan.AreaId} was not found for notification processing.");
        }

        var area = loadedArea.Value.Aggregate;
        var currentWeek = ResolveCurrentWeek(clock, area.CurrentWeekRule);
        if (plan.WeekId != currentWeek)
        {
            logger.LogDebug(
                "Skipping notification because plan week is not current week. PlanId={PlanId}, PlanWeek={PlanWeek}, CurrentWeek={CurrentWeek}",
                plan.Id,
                plan.WeekId,
                currentWeek);
            return [];
        }

        return BuildNotifications(eventId, source.Reason, plan, area);
    }

    private NotificationEventSource? DeserializeEvent(string routingKey, ReadOnlySpan<byte> body)
        => routingKey switch
        {
            "weekly-plan.published" => jsonSerializer.Deserialize<WeeklyPlanPublished>(body) is { } published
                ? new NotificationEventSource(published.PlanId, NotificationReason.Confirmed)
                : null,
            "weekly-plan.recalculated" => jsonSerializer.Deserialize<WeeklyPlanRecalculated>(body) is { } recalculated
                ? new NotificationEventSource(recalculated.PlanId, NotificationReason.Changed)
                : null,
            _ => null
        };

    private static IReadOnlyList<UserNotification> BuildNotifications(
        Guid eventId,
        NotificationReason reason,
        WeeklyDutyPlan plan,
        CleaningArea area)
    {
        var notifications = new List<UserNotification>();
        var spotNamesById = area.Spots.ToDictionary(x => x.Id, x => x.Name);

        foreach (var assignmentGroup in plan.Assignments
                     .GroupBy(x => x.UserId)
                     .OrderBy(group => group.Key.Value))
        {
            var spotNames = assignmentGroup
                .Select(assignment => spotNamesById.GetValueOrDefault(assignment.SpotId) ?? assignment.SpotId.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            var notificationType = reason == NotificationReason.Confirmed
                ? "weekly-duty-plan.assignment.confirmed"
                : "weekly-duty-plan.assignment.changed";
            var title = reason == NotificationReason.Confirmed
                ? "今週のお掃除当番が確定しました"
                : "今週のお掃除当番が変更されました";
            var body = reason == NotificationReason.Confirmed
                ? $"{area.Name} の {plan.WeekId} の担当は {string.Join("、", spotNames)} です。"
                : $"{area.Name} の {plan.WeekId} の担当が更新されました。担当箇所は {string.Join("、", spotNames)} です。";

            notifications.Add(new UserNotification(
                CreateNotificationId(eventId, assignmentGroup.Key, "assigned"),
                notificationType,
                assignmentGroup.Key.Value,
                title,
                body,
                BuildMetadata(plan, area, "assigned", spotNames)));
        }

        foreach (var offDuty in plan.OffDutyEntries
                     .OrderBy(entry => entry.UserId.Value))
        {
            var notificationType = reason == NotificationReason.Confirmed
                ? "weekly-duty-plan.off-duty.confirmed"
                : "weekly-duty-plan.off-duty.changed";
            var title = reason == NotificationReason.Confirmed
                ? "今週のお掃除当番が確定しました"
                : "今週のお掃除当番が変更されました";
            var body = reason == NotificationReason.Confirmed
                ? $"{area.Name} の {plan.WeekId} は担当なしです。"
                : $"{area.Name} の {plan.WeekId} の担当が更新され、担当なしになりました。";

            notifications.Add(new UserNotification(
                CreateNotificationId(eventId, offDuty.UserId, "off-duty"),
                notificationType,
                offDuty.UserId.Value,
                title,
                body,
                BuildMetadata(plan, area, "off-duty", [])));
        }

        return notifications;
    }

    private static string CreateNotificationId(Guid eventId, UserId userId, string state)
        => $"{eventId:N}:{userId.Value:N}:{state}";

    private static IReadOnlyDictionary<string, string> BuildMetadata(
        WeeklyDutyPlan plan,
        CleaningArea area,
        string assignmentState,
        IReadOnlyList<string> spotNames)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["planId"] = plan.Id.ToString(),
            ["areaId"] = area.Id.ToString(),
            ["areaName"] = area.Name,
            ["weekId"] = plan.WeekId.ToString(),
            ["revision"] = plan.Revision.Value.ToString(),
            ["assignmentState"] = assignmentState,
            ["spotNames"] = string.Join(",", spotNames)
        };
    }

    private static WeekId ResolveCurrentWeek(IClock clock, WeekRule weekRule)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(weekRule.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(clock.UtcNow, tz);
        return WeekId.FromDate(DateOnly.FromDateTime(localNow.Date));
    }

    private static bool TryReadEventId(IReadOnlyDictionary<string, object?> headers, out Guid eventId)
    {
        if (!headers.TryGetValue("event_id", out var raw) || raw is null)
        {
            eventId = Guid.Empty;
            return false;
        }

        switch (raw)
        {
            case Guid guid:
                eventId = guid;
                return true;
            case string text when Guid.TryParse(text, out var parsed):
                eventId = parsed;
                return true;
            case byte[] bytes when Guid.TryParse(Encoding.UTF8.GetString(bytes), out var parsed):
                eventId = parsed;
                return true;
            default:
                eventId = Guid.Empty;
                return false;
        }
    }

    private readonly record struct NotificationEventSource(
        WeeklyDutyPlanId PlanId,
        NotificationReason Reason);

    private enum NotificationReason
    {
        Confirmed = 0,
        Changed = 1
    }
}

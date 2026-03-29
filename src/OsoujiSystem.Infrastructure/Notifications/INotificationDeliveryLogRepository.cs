using OsoujiSystem.Application.Notifications;

namespace OsoujiSystem.Infrastructure.Notifications;

internal interface INotificationDeliveryLogRepository
{
    Task<bool> HasSucceededAsync(
        string channelName,
        string notificationId,
        CancellationToken ct);

    Task MarkSucceededAsync(
        string channelName,
        UserNotification notification,
        CancellationToken ct);
}

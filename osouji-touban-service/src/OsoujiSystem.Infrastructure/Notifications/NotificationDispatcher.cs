using Microsoft.Extensions.Logging;
using OsoujiSystem.Application.Notifications;

namespace OsoujiSystem.Infrastructure.Notifications;

internal sealed class NotificationDispatcher(
    IEnumerable<INotificationChannel> channels,
    INotificationDeliveryLogRepository deliveryLogRepository,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    private readonly INotificationChannel[] _channels = channels
        .OrderBy(channel => channel.ChannelName, StringComparer.Ordinal)
        .ToArray();

    public async Task DispatchAsync(IReadOnlyCollection<UserNotification> notifications, CancellationToken ct)
    {
        if (notifications.Count == 0)
        {
            return;
        }

        if (_channels.Length == 0)
        {
            logger.LogInformation(
                "Skipping notification dispatch because no notification channels are registered. Count={Count}",
                notifications.Count);
            return;
        }

        foreach (var notification in notifications
                     .OrderBy(x => x.RecipientUserId)
                     .ThenBy(x => x.NotificationId, StringComparer.Ordinal))
        {
            foreach (var channel in _channels)
            {
                if (await deliveryLogRepository.HasSucceededAsync(channel.ChannelName, notification.NotificationId, ct))
                {
                    continue;
                }

                await channel.SendAsync(notification, ct);
                await deliveryLogRepository.MarkSucceededAsync(channel.ChannelName, notification, ct);
            }
        }
    }
}

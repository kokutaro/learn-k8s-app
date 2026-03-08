using Dapper;
using Npgsql;
using OsoujiSystem.Application.Notifications;

namespace OsoujiSystem.Infrastructure.Notifications;

internal sealed class NotificationDeliveryLogRepository(NpgsqlDataSource dataSource) : INotificationDeliveryLogRepository
{
    public async Task<bool> HasSucceededAsync(string channelName, string notificationId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var count = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1)
            FROM notification_channel_deliveries
            WHERE channel_name = @channelName
              AND notification_id = @notificationId;
            """,
            new { channelName, notificationId });

        return count > 0;
    }

    public async Task MarkSucceededAsync(string channelName, UserNotification notification, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO notification_channel_deliveries (
                channel_name,
                notification_id,
                notification_type,
                recipient_user_id,
                title,
                delivered_at
            )
            VALUES (
                @channelName,
                @notificationId,
                @notificationType,
                @recipientUserId,
                @title,
                now()
            )
            ON CONFLICT (channel_name, notification_id) DO NOTHING;
            """,
            new
            {
                channelName,
                notificationId = notification.NotificationId,
                notificationType = notification.NotificationType,
                recipientUserId = notification.RecipientUserId,
                title = notification.Title
            });
    }
}

using Microsoft.Extensions.Logging;
using OsoujiSystem.Application.Notifications;

namespace OsoujiSystem.Infrastructure.Notifications;

public class EMailNotificationChannel(ILogger<EMailNotificationChannel> logger) : INotificationChannel
{
    public string ChannelName => "Email";

    public Task SendAsync(UserNotification notification, CancellationToken ct)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "EMail notification channel sent. NotificationId={NotificationId}, RecipientUserId={RecipientUserId}, Title={Title}, Body={Body}",
                notification.NotificationId,
                notification.RecipientUserId,
                notification.Title,
                notification.Body);
        }
        
        return Task.CompletedTask;
    }
}
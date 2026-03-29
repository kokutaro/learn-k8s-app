namespace OsoujiSystem.Application.Notifications;

public sealed record UserNotification(
    string NotificationId,
    string NotificationType,
    Guid RecipientUserId,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string> Metadata);

public interface INotificationDispatcher
{
    Task DispatchAsync(
        IReadOnlyCollection<UserNotification> notifications,
        CancellationToken ct);
}

public interface INotificationChannel
{
    string ChannelName { get; }

    Task SendAsync(
        UserNotification notification,
        CancellationToken ct);
}

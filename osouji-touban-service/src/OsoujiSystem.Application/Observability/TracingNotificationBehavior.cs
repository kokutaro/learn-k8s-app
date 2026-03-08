using System.Diagnostics;
using Cortex.Mediator.Notifications;

namespace OsoujiSystem.Application.Observability;

public sealed class TracingNotificationBehavior<TNotification> : INotificationPipelineBehavior<TNotification>
    where TNotification : INotification
{
    public async Task Handle(
        TNotification notification,
        NotificationHandlerDelegate next,
        CancellationToken cancellationToken)
    {
        using var activity = StartNotificationActivity(notification);

        try
        {
            await next();
        }
        catch (Exception ex)
        {
            if (activity is not null)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.AddEvent(new ActivityEvent(
                    "exception",
                    tags: new ActivityTagsCollection
                    {
                        { "exception.type", ex.GetType().FullName },
                        { "exception.message", ex.Message },
                        { "exception.stacktrace", ex.ToString() }
                    }));
            }

            throw;
        }
    }

    private static Activity? StartNotificationActivity(TNotification notification)
    {
        var notificationType = notification.GetType();
        var activity = ApplicationTelemetry.ActivitySource.StartActivity(
            $"mediator.notification {notificationType.Name}",
            ActivityKind.Internal);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("messaging.system", "cortex.mediator");
        activity.SetTag("messaging.operation", "process");
        activity.SetTag("mediator.notification.type", notificationType.FullName ?? notificationType.Name);
        activity.SetTag("code.namespace", notificationType.Namespace);
        activity.SetTag("code.function", notificationType.Name);

        return activity;
    }
}

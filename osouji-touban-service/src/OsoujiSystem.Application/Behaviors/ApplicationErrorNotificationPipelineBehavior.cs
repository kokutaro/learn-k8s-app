using Cortex.Mediator.Notifications;
using Microsoft.Extensions.Logging;

namespace OsoujiSystem.Application.Behaviors;

public sealed class ApplicationErrorNotificationPipelineBehavior<TNotification>(
    ILogger<ApplicationErrorNotificationPipelineBehavior<TNotification>> logger)
    : INotificationPipelineBehavior<TNotification>
    where TNotification : INotification
{
    public async Task Handle(
        TNotification notification,
        NotificationHandlerDelegate next,
        CancellationToken cancellationToken)
    {
        try
        {
            await next();
        }
        catch (Exception exception)
        {
            var error = ApplicationErrorMapping.FromException(exception);

            logger.LogError(
                exception,
                "Mediator notification {NotificationType} failed with application error {ErrorCode}.",
                typeof(TNotification).Name,
                error.Code);

            throw new ApplicationErrorException(error, exception);
        }
    }
}

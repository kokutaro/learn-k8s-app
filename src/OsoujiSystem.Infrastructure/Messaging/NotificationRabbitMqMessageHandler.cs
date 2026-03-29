using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OsoujiSystem.Application.Notifications;
using OsoujiSystem.Infrastructure.Notifications;

namespace OsoujiSystem.Infrastructure.Messaging;

internal sealed class NotificationRabbitMqMessageHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationRabbitMqMessageHandler> logger) : INotificationRabbitMqMessageHandler
{
    public async Task HandleAsync(
        string consumerName,
        string routingKey,
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, object?> headers,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<WeeklyPlanNotificationFactory>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        var notifications = await factory.BuildAsync(routingKey, body, headers, ct);
        if (notifications.Count == 0)
        {
            logger.LogDebug(
                "No notifications produced for routing key {RoutingKey}. Consumer={ConsumerName}",
                routingKey,
                consumerName);
            return;
        }

        await dispatcher.DispatchAsync(notifications, ct);
    }
}

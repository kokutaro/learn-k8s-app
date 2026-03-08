using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OsoujiSystem.Infrastructure.Messaging;

internal sealed class NotificationConsumerWorker(
    IConfiguration configuration,
    IConsumerProcessedEventRepository processedRepository,
    INotificationRabbitMqMessageHandler messageHandler,
    ILogger<NotificationConsumerWorker> logger) : RabbitMqConsumerWorkerBase<INotificationRabbitMqMessageHandler>(configuration, processedRepository, messageHandler, logger)
{
    protected override string ConsumerName => RabbitMqTopology.NotificationConsumer;

    protected override string QueueName => RabbitMqTopology.NotificationQueue;
}

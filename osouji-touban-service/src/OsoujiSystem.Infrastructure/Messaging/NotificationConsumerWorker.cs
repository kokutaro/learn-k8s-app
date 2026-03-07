using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OsoujiSystem.Infrastructure.Messaging;

internal sealed class NotificationConsumerWorker(
    IConfiguration configuration,
    IConsumerProcessedEventRepository processedRepository,
    IRabbitMqMessageHandler messageHandler,
    ILogger<NotificationConsumerWorker> logger) : RabbitMqConsumerWorkerBase(configuration, processedRepository, messageHandler, logger)
{
  protected override string ConsumerName => RabbitMqTopology.NotificationConsumer;

    protected override string QueueName => RabbitMqTopology.NotificationQueue;
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OsoujiSystem.Infrastructure.Options;

namespace OsoujiSystem.Infrastructure.Messaging;

internal sealed class NotificationConsumerWorker : RabbitMqConsumerWorkerBase
{
    public NotificationConsumerWorker(
        IOptions<InfrastructureOptions> options,
        IConsumerProcessedEventRepository processedRepository,
        IRabbitMqMessageHandler messageHandler,
        ILogger<NotificationConsumerWorker> logger)
        : base(options, processedRepository, messageHandler, logger)
    {
    }

    protected override string ConsumerName => RabbitMqTopology.NotificationConsumer;

    protected override string QueueName => RabbitMqTopology.NotificationQueue;
}

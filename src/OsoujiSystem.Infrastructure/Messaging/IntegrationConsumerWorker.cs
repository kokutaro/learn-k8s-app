using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OsoujiSystem.Infrastructure.Messaging;

internal sealed class IntegrationConsumerWorker(
    IConfiguration configuration,
    IConsumerProcessedEventRepository processedRepository,
    IIntegrationRabbitMqMessageHandler messageHandler,
    ILogger<IntegrationConsumerWorker> logger) : RabbitMqConsumerWorkerBase<IIntegrationRabbitMqMessageHandler>(configuration, processedRepository, messageHandler, logger)
{
    protected override string ConsumerName => RabbitMqTopology.IntegrationConsumer;

    protected override string QueueName => RabbitMqTopology.IntegrationQueue;
}

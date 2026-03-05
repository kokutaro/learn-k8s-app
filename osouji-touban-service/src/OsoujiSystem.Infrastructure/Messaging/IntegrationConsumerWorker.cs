using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OsoujiSystem.Infrastructure.Options;

namespace OsoujiSystem.Infrastructure.Messaging;

internal sealed class IntegrationConsumerWorker : RabbitMqConsumerWorkerBase
{
    public IntegrationConsumerWorker(
        IOptions<InfrastructureOptions> options,
        IConsumerProcessedEventRepository processedRepository,
        IRabbitMqMessageHandler messageHandler,
        ILogger<IntegrationConsumerWorker> logger)
        : base(options, processedRepository, messageHandler, logger)
    {
    }

    protected override string ConsumerName => RabbitMqTopology.IntegrationConsumer;

    protected override string QueueName => RabbitMqTopology.IntegrationQueue;
}

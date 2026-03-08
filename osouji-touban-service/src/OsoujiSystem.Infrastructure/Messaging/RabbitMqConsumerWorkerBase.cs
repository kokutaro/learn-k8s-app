using System.Text;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OsoujiSystem.Infrastructure.Observability;
using RabbitMQ.Client;

namespace OsoujiSystem.Infrastructure.Messaging;

internal abstract class RabbitMqConsumerWorkerBase<TMessageHandler>(
    IConfiguration configuration,
    IConsumerProcessedEventRepository processedRepository,
    TMessageHandler messageHandler,
    ILogger logger) : BackgroundService
    where TMessageHandler : IRabbitMqMessageHandler
{
    protected abstract string ConsumerName { get; }

    protected abstract string QueueName { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeLoopAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RabbitMQ consumer loop failed for {ConsumerName}.", ConsumerName);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var factory = RabbitMqConnectionFactoryProvider.Create(configuration);

        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        await RabbitMqTopology.DeclareAsync(channel, ct);
        await channel.BasicQosAsync(0, prefetchCount: 20, global: false, cancellationToken: ct);

        while (!ct.IsCancellationRequested)
        {
            var delivery = await channel.BasicGetAsync(QueueName, autoAck: false, cancellationToken: ct);
            if (delivery is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
                continue;
            }

            await HandleDeliveryAsync(channel, delivery, ct);
        }
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicGetResult delivery, CancellationToken ct)
    {
        var headers = NormalizeHeaders(delivery.BasicProperties.Headers);
        using var activity = RabbitMqTraceContext.TryExtractParentContext(headers, out var parentContext)
            ? OsoujiTelemetry.ActivitySource.StartActivity("rabbitmq.consume", ActivityKind.Consumer, parentContext)
            : OsoujiTelemetry.ActivitySource.StartActivity("rabbitmq.consume", ActivityKind.Consumer);

        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", QueueName);
        activity?.SetTag("messaging.rabbitmq.routing_key", delivery.RoutingKey);
        activity?.SetTag("messaging.message.id", delivery.BasicProperties.MessageId);
        activity?.SetTag("messaging.operation", "process");

        OsoujiTelemetry.RabbitConsumerMessagesTotal.Add(1, new KeyValuePair<string, object?>("consumer", ConsumerName));

        if (!TryReadEventId(headers, out var eventId))
        {
            logger.LogWarning("Skipping malformed message without event_id. Consumer={ConsumerName}", ConsumerName);
            await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
            return;
        }

        if (await processedRepository.IsProcessedAsync(ConsumerName, eventId, ct))
        {
            await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
            return;
        }

        try
        {
            await messageHandler.HandleAsync(ConsumerName, delivery.RoutingKey, delivery.Body, headers, ct);
            await processedRepository.MarkProcessedAsync(ConsumerName, eventId, ct);
            await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
        }
        catch (Exception ex)
        {
            OsoujiTelemetry.RabbitConsumerFailuresTotal.Add(1,
                new KeyValuePair<string, object?>("consumer", ConsumerName));

            var currentRetryCount = ReadRetryCount(headers);
            var nextRetryCount = currentRetryCount + 1;
            var destination = RabbitMqRetryPolicy.Resolve(ConsumerName, nextRetryCount);

            headers["x-retry-count"] = nextRetryCount;
            headers["last_error"] = ex.Message;

            try
            {
                using var republishActivity = OsoujiTelemetry.ActivitySource.StartActivity("rabbitmq.republish", ActivityKind.Producer);
                republishActivity?.SetTag("messaging.system", "rabbitmq");
                republishActivity?.SetTag("messaging.destination", destination.Exchange);
                republishActivity?.SetTag("messaging.rabbitmq.routing_key", destination.RoutingKey);
                republishActivity?.SetTag("messaging.message.id", delivery.BasicProperties.MessageId);

                RabbitMqTraceContext.Inject(republishActivity, headers);

                var props = new BasicProperties
                {
                    MessageId = delivery.BasicProperties.MessageId,
                    Persistent = true,
                    Headers = RabbitMqTraceContext.ToRabbitMqHeaders(headers)
                };

                await channel.BasicPublishAsync(
                    exchange: destination.Exchange,
                    routingKey: destination.RoutingKey,
                    mandatory: false,
                    basicProperties: props,
                    body: delivery.Body,
                    cancellationToken: ct);

                await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);

                if (destination.IsDlq)
                {
                    OsoujiTelemetry.RabbitMqDlqMessagesTotal.Add(
                        1,
                        new KeyValuePair<string, object?>("queue", RabbitMqTopology.GetDlqQueueName(ConsumerName)));
                    logger.LogWarning(ex,
                        "Message moved to DLQ. Consumer={ConsumerName}, EventId={EventId}, RetryCount={RetryCount}",
                        ConsumerName,
                        eventId,
                        nextRetryCount);
                }
            }
            catch (Exception publishEx)
            {
                logger.LogError(publishEx,
                    "Failed to republish message for retry/dlq. Consumer={ConsumerName}, EventId={EventId}",
                    ConsumerName,
                    eventId);

                await channel.BasicNackAsync(delivery.DeliveryTag, false, requeue: true, cancellationToken: ct);
            }
        }
    }

    internal static int ReadRetryCount(IReadOnlyDictionary<string, object?> headers)
    {
        if (!headers.TryGetValue("x-retry-count", out var raw) || raw is null)
        {
            return 0;
        }

        return raw switch
        {
            int asInt => asInt,
            long asLong => (int)asLong,
            string asString when int.TryParse(asString, out var parsed) => parsed,
            byte[] asBytes when int.TryParse(Encoding.UTF8.GetString(asBytes), out var parsed) => parsed,
            _ => 0
        };
    }

    internal static bool TryReadEventId(IReadOnlyDictionary<string, object?> headers, out Guid eventId)
    {
        if (!headers.TryGetValue("event_id", out var raw) || raw is null)
        {
            eventId = Guid.Empty;
            return false;
        }

        switch (raw)
        {
            case Guid guid:
                eventId = guid;
                return true;
            case string text when Guid.TryParse(text, out var parsedFromString):
                eventId = parsedFromString;
                return true;
            case byte[] bytes when Guid.TryParse(Encoding.UTF8.GetString(bytes), out var parsedFromBytes):
                eventId = parsedFromBytes;
                return true;
        }

        eventId = Guid.Empty;
        return false;
    }

    private static Dictionary<string, object?> NormalizeHeaders(IDictionary<string, object?>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return [];
        }

        return headers.ToDictionary(
            pair => pair.Key,
            pair => pair.Value switch
            {
                byte[] rawBytes => Encoding.UTF8.GetString(rawBytes),
                _ => pair.Value
            });
    }
}

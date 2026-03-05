using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OsoujiSystem.Infrastructure.Options;
using RabbitMQ.Client;

namespace OsoujiSystem.Infrastructure.Messaging;

internal abstract class RabbitMqConsumerWorkerBase : BackgroundService
{
    private readonly IOptions<InfrastructureOptions> _options;
    private readonly IConsumerProcessedEventRepository _processedRepository;
    private readonly IRabbitMqMessageHandler _messageHandler;
    private readonly ILogger _logger;

    protected RabbitMqConsumerWorkerBase(
        IOptions<InfrastructureOptions> options,
        IConsumerProcessedEventRepository processedRepository,
        IRabbitMqMessageHandler messageHandler,
        ILogger logger)
    {
        _options = options;
        _processedRepository = processedRepository;
        _messageHandler = messageHandler;
        _logger = logger;
    }

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
                _logger.LogError(ex, "RabbitMQ consumer loop failed for {ConsumerName}.", ConsumerName);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var rabbitOptions = _options.Value.RabbitMq;
        var factory = new ConnectionFactory
        {
            HostName = rabbitOptions.Host,
            Port = rabbitOptions.Port,
            VirtualHost = rabbitOptions.VirtualHost,
            UserName = rabbitOptions.Username,
            Password = rabbitOptions.Password
        };

        using var connection = await factory.CreateConnectionAsync(ct);
        using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

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

        if (!TryReadEventId(headers, out var eventId))
        {
            _logger.LogWarning("Skipping malformed message without event_id. Consumer={ConsumerName}", ConsumerName);
            await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
            return;
        }

        if (await _processedRepository.IsProcessedAsync(ConsumerName, eventId, ct))
        {
            await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
            return;
        }

        try
        {
            await _messageHandler.HandleAsync(ConsumerName, delivery.RoutingKey, delivery.Body, headers, ct);
            await _processedRepository.MarkProcessedAsync(ConsumerName, eventId, ct);
            await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
        }
        catch (Exception ex)
        {
            var currentRetryCount = ReadRetryCount(headers);
            var nextRetryCount = currentRetryCount + 1;
            var destination = RabbitMqRetryPolicy.Resolve(ConsumerName, nextRetryCount);

            headers["x-retry-count"] = nextRetryCount;
            headers["last_error"] = ex.Message;

            try
            {
                var props = new BasicProperties
                {
                    MessageId = delivery.BasicProperties.MessageId,
                    Persistent = true,
                    Headers = headers.ToDictionary(k => k.Key, v => v.Value)
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
                    _logger.LogWarning(ex,
                        "Message moved to DLQ. Consumer={ConsumerName}, EventId={EventId}, RetryCount={RetryCount}",
                        ConsumerName,
                        eventId,
                        nextRetryCount);
                }
            }
            catch (Exception publishEx)
            {
                _logger.LogError(publishEx,
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
            eventId = default;
            return false;
        }

        if (raw is Guid guid)
        {
            eventId = guid;
            return true;
        }

        if (raw is string text && Guid.TryParse(text, out var parsedFromString))
        {
            eventId = parsedFromString;
            return true;
        }

        if (raw is byte[] bytes && Guid.TryParse(Encoding.UTF8.GetString(bytes), out var parsedFromBytes))
        {
            eventId = parsedFromBytes;
            return true;
        }

        eventId = default;
        return false;
    }

    private static Dictionary<string, object?> NormalizeHeaders(IDictionary<string, object?>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return new Dictionary<string, object?>();
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

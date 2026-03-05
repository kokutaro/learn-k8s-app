using RabbitMQ.Client;

namespace OsoujiSystem.Infrastructure.Messaging;

internal static class RabbitMqTopology
{
    internal const string EventsExchange = "osouji.domain.events.v1";
    internal const string RetryExchange = "osouji.domain.retry.v1";
    internal const string DlqExchange = "osouji.domain.dlq.v1";

    internal const string NotificationConsumer = "notification";
    internal const string IntegrationConsumer = "integration";

    internal const string NotificationQueue = "q.notification.v1";
    internal const string NotificationRetry1mQueue = "q.notification.retry.1m";
    internal const string NotificationRetry5mQueue = "q.notification.retry.5m";
    internal const string NotificationRetry30mQueue = "q.notification.retry.30m";
    internal const string NotificationDlqQueue = "q.notification.dlq.v1";

    internal const string IntegrationQueue = "q.integration.v1";
    internal const string IntegrationRetry1mQueue = "q.integration.retry.1m";
    internal const string IntegrationRetry5mQueue = "q.integration.retry.5m";
    internal const string IntegrationRetry30mQueue = "q.integration.retry.30m";
    internal const string IntegrationDlqQueue = "q.integration.dlq.v1";

    private const int Retry1mTtlMs = 60_000;
    private const int Retry5mTtlMs = 300_000;
    private const int Retry30mTtlMs = 1_800_000;
    private const long DlqTtlMs = 2_592_000_000L;

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(EventsExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(RetryExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DlqExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);

        await channel.QueueDeclareAsync(NotificationQueue, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: ct);
        await channel.QueueDeclareAsync(NotificationRetry1mQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: BuildRetryArgs(EventsExchange, "notification.redeliver", Retry1mTtlMs), cancellationToken: ct);
        await channel.QueueDeclareAsync(NotificationRetry5mQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: BuildRetryArgs(EventsExchange, "notification.redeliver", Retry5mTtlMs), cancellationToken: ct);
        await channel.QueueDeclareAsync(NotificationRetry30mQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: BuildRetryArgs(EventsExchange, "notification.redeliver", Retry30mTtlMs), cancellationToken: ct);
        await channel.QueueDeclareAsync(NotificationDlqQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object> { ["x-message-ttl"] = DlqTtlMs }, cancellationToken: ct);

        await channel.QueueDeclareAsync(IntegrationQueue, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: ct);
        await channel.QueueDeclareAsync(IntegrationRetry1mQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: BuildRetryArgs(EventsExchange, "integration.redeliver", Retry1mTtlMs), cancellationToken: ct);
        await channel.QueueDeclareAsync(IntegrationRetry5mQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: BuildRetryArgs(EventsExchange, "integration.redeliver", Retry5mTtlMs), cancellationToken: ct);
        await channel.QueueDeclareAsync(IntegrationRetry30mQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: BuildRetryArgs(EventsExchange, "integration.redeliver", Retry30mTtlMs), cancellationToken: ct);
        await channel.QueueDeclareAsync(IntegrationDlqQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object> { ["x-message-ttl"] = DlqTtlMs }, cancellationToken: ct);

        await channel.QueueBindAsync(NotificationQueue, EventsExchange, "weekly-plan.*", cancellationToken: ct);
        await channel.QueueBindAsync(NotificationQueue, EventsExchange, "notification.redeliver", cancellationToken: ct);

        await channel.QueueBindAsync(IntegrationQueue, EventsExchange, "weekly-plan.*", cancellationToken: ct);
        await channel.QueueBindAsync(IntegrationQueue, EventsExchange, "cleaning-area.*", cancellationToken: ct);
        await channel.QueueBindAsync(IntegrationQueue, EventsExchange, "integration.redeliver", cancellationToken: ct);

        await channel.QueueBindAsync(NotificationRetry1mQueue, RetryExchange, "notification.retry.1m", cancellationToken: ct);
        await channel.QueueBindAsync(NotificationRetry5mQueue, RetryExchange, "notification.retry.5m", cancellationToken: ct);
        await channel.QueueBindAsync(NotificationRetry30mQueue, RetryExchange, "notification.retry.30m", cancellationToken: ct);

        await channel.QueueBindAsync(IntegrationRetry1mQueue, RetryExchange, "integration.retry.1m", cancellationToken: ct);
        await channel.QueueBindAsync(IntegrationRetry5mQueue, RetryExchange, "integration.retry.5m", cancellationToken: ct);
        await channel.QueueBindAsync(IntegrationRetry30mQueue, RetryExchange, "integration.retry.30m", cancellationToken: ct);

        await channel.QueueBindAsync(NotificationDlqQueue, DlqExchange, "notification.dlq", cancellationToken: ct);
        await channel.QueueBindAsync(IntegrationDlqQueue, DlqExchange, "integration.dlq", cancellationToken: ct);
    }

    private static IDictionary<string, object> BuildRetryArgs(string deadLetterExchange, string deadLetterRoutingKey, int ttlMs)
        => new Dictionary<string, object>
        {
            ["x-message-ttl"] = ttlMs,
            ["x-dead-letter-exchange"] = deadLetterExchange,
            ["x-dead-letter-routing-key"] = deadLetterRoutingKey
        };
}

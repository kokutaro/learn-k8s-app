namespace OsoujiSystem.Infrastructure.Messaging;

internal static class RabbitMqRetryPolicy
{
    public const int MaxRetryCount = 5;

    public static RetryDestination Resolve(string consumerName, int nextRetryCount)
    {
        if (nextRetryCount >= MaxRetryCount)
        {
            return new RetryDestination(
                RabbitMqTopology.DlqExchange,
                $"{consumerName}.dlq",
                true);
        }

        var stageRoutingKey = nextRetryCount switch
        {
            1 => $"{consumerName}.retry.1m",
            2 or 3 => $"{consumerName}.retry.5m",
            _ => $"{consumerName}.retry.30m"
        };

        return new RetryDestination(
            RabbitMqTopology.RetryExchange,
            stageRoutingKey,
            false);
    }
}

internal readonly record struct RetryDestination(
    string Exchange,
    string RoutingKey,
    bool IsDlq);

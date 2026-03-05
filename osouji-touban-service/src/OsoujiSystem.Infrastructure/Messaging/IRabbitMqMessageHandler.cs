namespace OsoujiSystem.Infrastructure.Messaging;

internal interface IRabbitMqMessageHandler
{
    Task HandleAsync(
        string consumerName,
        string routingKey,
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, object?> headers,
        CancellationToken ct);
}

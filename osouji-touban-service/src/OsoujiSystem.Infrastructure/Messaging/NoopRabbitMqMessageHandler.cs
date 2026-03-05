namespace OsoujiSystem.Infrastructure.Messaging;

internal sealed class NoopRabbitMqMessageHandler : IRabbitMqMessageHandler
{
    public Task HandleAsync(
        string consumerName,
        string routingKey,
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, object?> headers,
        CancellationToken ct)
        => Task.CompletedTask;
}

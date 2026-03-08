namespace OsoujiSystem.Infrastructure.Messaging;

internal sealed class NoopIntegrationRabbitMqMessageHandler : IIntegrationRabbitMqMessageHandler
{
    public Task HandleAsync(
        string consumerName,
        string routingKey,
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, object?> headers,
        CancellationToken ct)
        => Task.CompletedTask;
}

namespace OsoujiSystem.Infrastructure.Messaging;

internal interface IConsumerProcessedEventRepository
{
    Task<bool> IsProcessedAsync(string consumerName, Guid eventId, CancellationToken ct);

    Task MarkProcessedAsync(string consumerName, Guid eventId, CancellationToken ct);
}

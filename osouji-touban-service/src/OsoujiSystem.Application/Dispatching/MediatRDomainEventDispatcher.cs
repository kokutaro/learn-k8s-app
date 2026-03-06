using Cortex.Mediator;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Abstractions;

namespace OsoujiSystem.Application.Dispatching;

public sealed class MediatRDomainEventDispatcher(IMediator publisher) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken ct)
    {
        foreach (var domainEvent in events)
        {
            await publisher.PublishAsync(new DomainEventNotification(domainEvent), ct);
        }
    }
}

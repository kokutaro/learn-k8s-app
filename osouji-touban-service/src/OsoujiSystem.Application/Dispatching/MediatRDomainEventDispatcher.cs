using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Abstractions;

namespace OsoujiSystem.Application.Dispatching;

public sealed class MediatRDomainEventDispatcher(IPublisher publisher) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken ct)
    {
        foreach (var domainEvent in events)
        {
            await publisher.Publish(new DomainEventNotification(domainEvent), ct);
        }
    }
}

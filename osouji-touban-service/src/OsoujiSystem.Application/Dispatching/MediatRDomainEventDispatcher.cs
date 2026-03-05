using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Abstractions;

namespace OsoujiSystem.Application.Dispatching;

public sealed class MediatRDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IPublisher _publisher;

    public MediatRDomainEventDispatcher(IPublisher publisher)
    {
        _publisher = publisher;
    }

    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken ct)
    {
        foreach (var domainEvent in events)
        {
            await _publisher.Publish(new DomainEventNotification(domainEvent), ct);
        }
    }
}

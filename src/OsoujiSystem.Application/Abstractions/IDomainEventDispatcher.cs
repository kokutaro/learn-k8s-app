using OsoujiSystem.Domain.Abstractions;

namespace OsoujiSystem.Application.Abstractions;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken ct);
}

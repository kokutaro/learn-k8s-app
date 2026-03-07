using OsoujiSystem.Domain.Abstractions;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal interface IEventWriteContextAccessor
{
    void Initialize();
    void Register(IDomainEvent domainEvent, Guid eventId);
    bool TryGetEventId(IDomainEvent domainEvent, out Guid eventId);
    void Clear();
}

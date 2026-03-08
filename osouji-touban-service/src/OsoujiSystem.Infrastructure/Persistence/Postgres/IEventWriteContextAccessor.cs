using OsoujiSystem.Domain.Abstractions;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal readonly record struct EventWriteMetadata(Guid EventId, long StreamVersion);

internal interface IEventWriteContextAccessor
{
    void Initialize();
    void Register(IDomainEvent domainEvent, Guid eventId, long streamVersion);
    bool TryGetMetadata(IDomainEvent domainEvent, out EventWriteMetadata metadata);
    void Clear();
}

using OsoujiSystem.Domain.Abstractions;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal readonly record struct EventWriteMetadata(Guid EventId, long StreamVersion, long GlobalPosition);

internal interface IEventWriteContextAccessor
{
    void Initialize();
    void Register(IDomainEvent domainEvent, Guid eventId, long streamVersion, long globalPosition);
    bool TryGetMetadata(IDomainEvent domainEvent, out EventWriteMetadata metadata);
    bool TryGetMaxGlobalPosition(out long globalPosition);
    void Clear();
}

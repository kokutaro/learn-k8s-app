using OsoujiSystem.Domain.Abstractions;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class AsyncLocalEventWriteContextAccessor : IEventWriteContextAccessor
{
    private readonly AsyncLocal<Dictionary<IDomainEvent, EventWriteMetadata>?> _context = new();

    public void Initialize() => _context.Value ??= new Dictionary<IDomainEvent, EventWriteMetadata>(ReferenceEqualityComparer.Instance);

    public void Register(IDomainEvent domainEvent, Guid eventId, long streamVersion)
    {
        Initialize();
        var map = _context.Value!;
        map[domainEvent] = new EventWriteMetadata(eventId, streamVersion);
    }

    public bool TryGetMetadata(IDomainEvent domainEvent, out EventWriteMetadata metadata)
    {
        var map = _context.Value;
        if (map is not null)
        {
            return map.TryGetValue(domainEvent, out metadata);
        }

        metadata = default;
        return false;
    }

    public void Clear() => _context.Value = null;
}

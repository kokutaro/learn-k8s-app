using OsoujiSystem.Domain.Abstractions;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class AsyncLocalEventWriteContextAccessor : IEventWriteContextAccessor
{
    private readonly AsyncLocal<Dictionary<IDomainEvent, Guid>?> _context = new();

    public void Initialize() => _context.Value ??= new Dictionary<IDomainEvent, Guid>(ReferenceEqualityComparer.Instance);

    public void Register(IDomainEvent domainEvent, Guid eventId)
    {
        Initialize();
        var map = _context.Value!;
        map[domainEvent] = eventId;
    }

    public bool TryGetEventId(IDomainEvent domainEvent, out Guid eventId)
    {
        var map = _context.Value;
        if (map is not null)
        {
            return map.TryGetValue(domainEvent, out eventId);
        }

        eventId = Guid.Empty;
        return false;
    }

    public void Clear() => _context.Value = null;
}

using System.Collections.Generic;
using System.Threading;
using OsoujiSystem.Domain.Abstractions;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class AsyncLocalEventWriteContextAccessor : IEventWriteContextAccessor
{
    private readonly AsyncLocal<Dictionary<IDomainEvent, Guid>?> _context = new();

    public void Register(IDomainEvent domainEvent, Guid eventId)
    {
        var map = _context.Value;
        if (map is null)
        {
            map = new Dictionary<IDomainEvent, Guid>(ReferenceEqualityComparer.Instance);
            _context.Value = map;
        }

        map[domainEvent] = eventId;
    }

    public bool TryGetEventId(IDomainEvent domainEvent, out Guid eventId)
    {
        var map = _context.Value;
        if (map is null)
        {
            eventId = default;
            return false;
        }

        return map.TryGetValue(domainEvent, out eventId);
    }

    public void Clear() => _context.Value = null;
}

namespace OsoujiSystem.Domain.Abstractions;

public abstract class AggregateRoot<TId>
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot()
    {
        Id = default!;
    }

    protected AggregateRoot(TId id)
    {
        Id = id;
    }

    public TId Id { get; protected init; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}

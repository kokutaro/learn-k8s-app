namespace OsoujiSystem.Domain.Abstractions;

public abstract class AggregateRoot<TId>(TId id)
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot() : this(default!)
    {
    }

    public TId Id { get; protected init; } = id;

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
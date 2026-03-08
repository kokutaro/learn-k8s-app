using Cortex.Mediator.Notifications;
using OsoujiSystem.Domain.Abstractions;

namespace OsoujiSystem.Application.Dispatching;

public sealed record DomainEventNotification(IDomainEvent DomainEvent) : INotification;

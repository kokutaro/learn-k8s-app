using System.Text.Json;
using Dapper;
using MediatR;
using Npgsql;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.Dispatching;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Infrastructure.Persistence.Postgres;

namespace OsoujiSystem.Infrastructure.Outbox;

internal sealed class OutboxDomainEventDispatcher : IDomainEventDispatcher
{
    private const string ExchangeName = "osouji.domain.events.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITransactionContextAccessor _transactionContextAccessor;
    private readonly IEventWriteContextAccessor _eventWriteContextAccessor;
    private readonly IPublisher _publisher;

    public OutboxDomainEventDispatcher(
        ITransactionContextAccessor transactionContextAccessor,
        IEventWriteContextAccessor eventWriteContextAccessor,
        IPublisher publisher)
    {
        _transactionContextAccessor = transactionContextAccessor;
        _eventWriteContextAccessor = eventWriteContextAccessor;
        _publisher = publisher;
    }

    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken ct)
    {
        if (!_transactionContextAccessor.HasActiveTransaction)
        {
            throw new InvalidOperationException("Outbox dispatcher requires an active transaction.");
        }

        var connection = _transactionContextAccessor.Connection!;
        var transaction = _transactionContextAccessor.Transaction!;

        foreach (var domainEvent in events)
        {
            if (!_eventWriteContextAccessor.TryGetEventId(domainEvent, out var sourceEventId))
            {
                continue;
            }

            var messageId = Guid.NewGuid();
            var routingKey = GetRoutingKey(domainEvent);
            var payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), JsonOptions);
            var headers = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["message_id"] = messageId,
                ["event_id"] = sourceEventId,
                ["event_type"] = domainEvent.GetType().Name,
                ["event_schema_version"] = 1,
                ["occurred_at"] = domainEvent.OccurredAt,
                ["trace_id"] = null,
                ["correlation_id"] = null,
                ["causation_id"] = null,
                ["x-retry-count"] = 0
            }, JsonOptions);

            await connection.ExecuteAsync(
                """
                INSERT INTO outbox_messages (
                    message_id,
                    source_event_id,
                    exchange_name,
                    routing_key,
                    payload,
                    headers,
                    available_at,
                    created_at
                )
                VALUES (
                    @messageId,
                    @sourceEventId,
                    @exchangeName,
                    @routingKey,
                    CAST(@payload AS jsonb),
                    CAST(@headers AS jsonb),
                    now(),
                    now()
                )
                ON CONFLICT (source_event_id) DO NOTHING;
                """,
                new
                {
                    messageId,
                    sourceEventId,
                    exchangeName = ExchangeName,
                    routingKey,
                    payload,
                    headers
                },
                transaction: transaction);

            await _publisher.Publish(new DomainEventNotification(domainEvent), ct);
        }
    }

    internal static string GetRoutingKey(IDomainEvent domainEvent)
        => domainEvent switch
        {
            WeeklyPlanGenerated => "weekly-plan.generated",
            WeeklyPlanRecalculated => "weekly-plan.recalculated",
            WeeklyPlanPublished => "weekly-plan.published",
            WeeklyPlanClosed => "weekly-plan.closed",
            CleaningSpotAdded => "cleaning-area.spot-added",
            CleaningSpotRemoved => "cleaning-area.spot-removed",
            UserAssignedToArea => "cleaning-area.user-assigned",
            UserUnassignedFromArea => "cleaning-area.user-unassigned",
            _ => "domain.unknown"
        };
}

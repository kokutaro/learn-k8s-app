using System.Text.Json;
using Dapper;
using MediatR;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.Dispatching;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Infrastructure.Messaging;
using OsoujiSystem.Infrastructure.Persistence.Postgres;

namespace OsoujiSystem.Infrastructure.Outbox;

internal sealed class OutboxDomainEventDispatcher(
    ITransactionContextAccessor transactionContextAccessor,
    IEventWriteContextAccessor eventWriteContextAccessor,
    IPublisher publisher) : IDomainEventDispatcher
{
    private const string ExchangeName = RabbitMqTopology.EventsExchange;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken ct)
    {
        if (!transactionContextAccessor.HasActiveTransaction)
        {
            throw new InvalidOperationException("Outbox dispatcher requires an active transaction.");
        }

        var connection = transactionContextAccessor.Connection!;
        var transaction = transactionContextAccessor.Transaction!;

        foreach (var domainEvent in events)
        {
            if (!eventWriteContextAccessor.TryGetEventId(domainEvent, out var sourceEventId))
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

            await publisher.Publish(new DomainEventNotification(domainEvent), ct);
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

using System.Diagnostics;
using System.Text.Json.Serialization;
using Dapper;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.Dispatching;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Infrastructure.Messaging;
using OsoujiSystem.Infrastructure.Persistence.Postgres;
using OsoujiSystem.Infrastructure.Serialization;
using Cortex.Mediator;
// ReSharper disable NotAccessedPositionalProperty.Local

namespace OsoujiSystem.Infrastructure.Outbox;

internal sealed class OutboxDomainEventDispatcher(
    ITransactionContextAccessor transactionContextAccessor,
    IEventWriteContextAccessor eventWriteContextAccessor,
    IMediator publisher,
    InfrastructureJsonSerializer jsonSerializer) : IDomainEventDispatcher
{
    private const string ExchangeName = RabbitMqTopology.EventsExchange;

    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken ct)
    {
        if (!transactionContextAccessor.HasActiveTransaction)
        {
            throw new InvalidOperationException("Outbox dispatcher requires an active transaction.");
        }

        var connection = transactionContextAccessor.Connection!;
        var transaction = transactionContextAccessor.Transaction!;
        var pendingMessages = new List<PendingOutboxMessage>(events.Count);
        var ordinal = 0;

        foreach (var domainEvent in events)
        {
            if (!eventWriteContextAccessor.TryGetMetadata(domainEvent, out var metadata))
            {
                continue;
            }

            var messageId = Guid.NewGuid();
            var routingKey = GetRoutingKey(domainEvent);
            var payload = jsonSerializer.Serialize(domainEvent, domainEvent.GetType());
            var headers = new Dictionary<string, object?>
            {
                ["message_id"] = messageId,
                ["event_id"] = metadata.EventId,
                ["event_type"] = domainEvent.GetType().Name,
                ["event_schema_version"] = 1,
                ["occurred_at"] = domainEvent.OccurredAt,
                ["aggregate_version"] = metadata.StreamVersion,
                ["trace_id"] = null,
                ["correlation_id"] = null,
                ["causation_id"] = null,
                ["x-retry-count"] = 0
            };

            RabbitMqTraceContext.Inject(Activity.Current, headers);

            pendingMessages.Add(new PendingOutboxMessage(
                domainEvent,
                new OutboxInsertRow(
                    ordinal++,
                    messageId,
                    metadata.EventId,
                    ExchangeName,
                    routingKey,
                    payload,
                    jsonSerializer.Serialize(headers))));
        }

        if (pendingMessages.Count > 0)
        {
            await connection.ExecuteAsync(
                """
                WITH outbox_rows AS (
                    SELECT ordinal,
                           message_id,
                           source_event_id,
                           exchange_name,
                           routing_key,
                           payload,
                           headers
                    FROM jsonb_to_recordset(CAST(@rows AS jsonb)) AS x(
                        ordinal integer,
                        message_id uuid,
                        source_event_id uuid,
                        exchange_name text,
                        routing_key text,
                        payload text,
                        headers text
                    )
                )
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
                SELECT message_id,
                       source_event_id,
                       exchange_name,
                       routing_key,
                       CAST(payload AS jsonb),
                       CAST(headers AS jsonb),
                       now(),
                       now()
                FROM outbox_rows
                ORDER BY ordinal
                ON CONFLICT (source_event_id) DO NOTHING;
                """,
                new
                {
                    rows = jsonSerializer.Serialize(
                        pendingMessages.Select(x => x.Row).ToArray())
                },
                transaction: transaction);
        }

        foreach (var pendingMessage in pendingMessages)
        {
            await publisher.PublishAsync(new DomainEventNotification(pendingMessage.DomainEvent), ct);
        }
    }

    internal static string GetRoutingKey(IDomainEvent domainEvent)
        => domainEvent switch
        {
            WeeklyPlanGenerated => "weekly-plan.generated",
            WeeklyPlanRecalculated => "weekly-plan.recalculated",
            WeeklyPlanPublished => "weekly-plan.published",
            DutyAssigned => "weekly-plan.duty.assigned",
            DutyReassigned => "weekly-plan.duty.reassigned",
            WeeklyPlanClosed => "weekly-plan.closed",
            CleaningAreaRegistered => "cleaning-area.registered",
            CleaningSpotAdded => "cleaning-area.spot-added",
            CleaningSpotRemoved => "cleaning-area.spot-removed",
            UserAssignedToArea => "cleaning-area.user-assigned",
            UserUnassignedFromArea => "cleaning-area.user-unassigned",
            FacilityRegistered => "facility-structure.facility-registered",
            FacilityUpdated => "facility-structure.facility-updated",
            UserRegistered => "user-registry.user-registered",
            UserUpdated => "user-registry.user-updated",
            _ => "domain.unknown"
        };

    private sealed record PendingOutboxMessage(IDomainEvent DomainEvent, OutboxInsertRow Row);

    private sealed record OutboxInsertRow(
        [property: JsonPropertyName("ordinal")] int Ordinal,
        [property: JsonPropertyName("message_id")] Guid MessageId,
        [property: JsonPropertyName("source_event_id")] Guid SourceEventId,
        // ReSharper disable once MemberHidesStaticFromOuterClass
        [property: JsonPropertyName("exchange_name")] string ExchangeName,
        [property: JsonPropertyName("routing_key")] string RoutingKey,
        [property: JsonPropertyName("payload")] string Payload,
        [property: JsonPropertyName("headers")] string Headers);
}

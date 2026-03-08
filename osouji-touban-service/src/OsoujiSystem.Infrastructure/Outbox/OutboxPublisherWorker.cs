using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OsoujiSystem.Infrastructure.Messaging;
using OsoujiSystem.Infrastructure.Observability;
using Npgsql;
using OsoujiSystem.Infrastructure.Options;
using RabbitMQ.Client;

namespace OsoujiSystem.Infrastructure.Outbox;

internal sealed class OutboxPublisherWorker(
    NpgsqlDataSource dataSource,
    IConfiguration configuration,
    IOptions<InfrastructureOptions> options,
    ILogger<OutboxPublisherWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(options.Value.Outbox.PollIntervalMs);
        using var timer = new PeriodicTimer(pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox publish batch failed.");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task PublishBatchAsync(CancellationToken ct)
    {
        using var activity = OsoujiTelemetry.ActivitySource.StartActivity("outbox.publish_batch");

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var batch = (await connection.QueryAsync<OutboxRow>(
            """
            SELECT message_id AS MessageId,
                   exchange_name AS ExchangeName,
                   routing_key AS RoutingKey,
                   payload::text AS Payload,
                   headers::text AS Headers,
                   created_at AS CreatedAt,
                   attempt_count AS AttemptCount
            FROM outbox_messages
            WHERE published_at IS NULL
              AND available_at <= now()
            ORDER BY created_at ASC
            LIMIT @batchSize;
            """,
            new { batchSize = options.Value.Outbox.BatchSize })).ToArray();

        if (batch.Length == 0)
        {
            activity?.SetTag("outbox.batch.count", 0);
            return;
        }
        activity?.SetTag("outbox.batch.count", batch.Length);

        var factory = RabbitMqConnectionFactoryProvider.Create(configuration);

        await using var rabbitConnection = await factory.CreateConnectionAsync(ct);
        await using var channel = await rabbitConnection.CreateChannelAsync(cancellationToken: ct);
        await RabbitMqTopology.DeclareAsync(channel, ct);
        var publishedMessageIds = new List<Guid>(batch.Length);
        var failedUpdates = new List<FailedOutboxUpdateRow>();

        foreach (var row in batch)
        {
            try
            {
                var properties = new BasicProperties();
                var headerMap = RabbitMqTraceContext.DeserializePersistedHeaders(row.Headers);

                using var publishActivity = RabbitMqTraceContext.TryExtractParentContext(headerMap, out var parentContext)
                    ? OsoujiTelemetry.ActivitySource.StartActivity("rabbitmq.publish", ActivityKind.Producer, parentContext)
                    : OsoujiTelemetry.ActivitySource.StartActivity("rabbitmq.publish", ActivityKind.Producer);

                publishActivity?.SetTag("messaging.system", "rabbitmq");
                publishActivity?.SetTag("messaging.destination", row.ExchangeName);
                publishActivity?.SetTag("messaging.rabbitmq.routing_key", row.RoutingKey);
                publishActivity?.SetTag("messaging.message.id", row.MessageId.ToString("D"));

                RabbitMqTraceContext.Inject(publishActivity, headerMap);

                properties.Headers = RabbitMqTraceContext.ToRabbitMqHeaders(headerMap);

                properties.MessageId = row.MessageId.ToString("D");
                properties.Persistent = true;

                var body = System.Text.Encoding.UTF8.GetBytes(row.Payload);
                await channel.BasicPublishAsync(
                    exchange: row.ExchangeName,
                    routingKey: row.RoutingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: ct);

                publishedMessageIds.Add(row.MessageId);

                var publishLagSeconds = Math.Max(0, (DateTimeOffset.UtcNow - row.CreatedAt).TotalSeconds);
                OsoujiTelemetry.OutboxPublishLagSeconds.Record(publishLagSeconds);
            }
            catch (Exception ex)
            {
                var nextAttempt = row.AttemptCount + 1;
                var nextAvailableAt = DateTimeOffset.UtcNow.AddMinutes(Math.Min(30, Math.Max(1, nextAttempt * 5)));
                failedUpdates.Add(new FailedOutboxUpdateRow(row.MessageId, ex.Message, nextAvailableAt));

                logger.LogWarning(ex, "Outbox publish failed for message {MessageId}", row.MessageId);
            }
        }

        if (publishedMessageIds.Count > 0)
        {
            await connection.ExecuteAsync(
                """
                UPDATE outbox_messages
                SET published_at = now(),
                    attempt_count = attempt_count + 1,
                    last_error = NULL
                WHERE message_id = ANY(@messageIds);
                """,
                new { messageIds = publishedMessageIds.ToArray() });
        }

        if (failedUpdates.Count > 0)
        {
            await connection.ExecuteAsync(
                """
                WITH failed_rows AS (
                    SELECT message_id,
                           last_error,
                           next_available_at
                    FROM jsonb_to_recordset(CAST(@rows AS jsonb)) AS x(
                        message_id uuid,
                        last_error text,
                        next_available_at timestamptz
                    )
                )
                UPDATE outbox_messages AS target
                SET attempt_count = target.attempt_count + 1,
                    last_error = failed_rows.last_error,
                    available_at = failed_rows.next_available_at
                FROM failed_rows
                WHERE target.message_id = failed_rows.message_id;
                """,
                new
                {
                    rows = JsonSerializer.Serialize(failedUpdates, JsonOptions)
                });
        }
    }

    private sealed record OutboxRow(
        Guid MessageId,
        string ExchangeName,
        string RoutingKey,
        string Payload,
        string Headers,
        DateTime CreatedAt,
        int AttemptCount);

    private sealed record FailedOutboxUpdateRow(
        [property: JsonPropertyName("message_id")] Guid MessageId,
        [property: JsonPropertyName("last_error")] string LastError,
        [property: JsonPropertyName("next_available_at")] DateTimeOffset NextAvailableAt);
}

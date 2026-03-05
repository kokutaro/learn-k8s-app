using System.Text.Json;
using Dapper;
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
    IOptions<InfrastructureOptions> options,
    ILogger<OutboxPublisherWorker> logger) : BackgroundService
{
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
        var rabbitOptions = options.Value.RabbitMq;

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

        var factory = new ConnectionFactory
        {
            HostName = rabbitOptions.Host!,
            Port = rabbitOptions.Port,
            VirtualHost = rabbitOptions.VirtualHost,
            UserName = rabbitOptions.Username!,
            Password = rabbitOptions.Password!
        };

        await using var rabbitConnection = await factory.CreateConnectionAsync(ct);
        await using var channel = await rabbitConnection.CreateChannelAsync(cancellationToken: ct);
        await RabbitMqTopology.DeclareAsync(channel, ct);

        foreach (var row in batch)
        {
            try
            {
                var properties = new BasicProperties();
                var headerMap = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.Headers)
                    ?? [];

                properties.Headers = headerMap
                    .Where(x => x.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    .ToDictionary(
                        x => x.Key, object? (x) => x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString()! : x.Value.GetRawText());

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

                await connection.ExecuteAsync(
                    """
                    UPDATE outbox_messages
                    SET published_at = now(),
                        attempt_count = attempt_count + 1,
                        last_error = NULL
                    WHERE message_id = @messageId;
                    """,
                    new { messageId = row.MessageId });

                var publishLagSeconds = Math.Max(0, (DateTimeOffset.UtcNow - row.CreatedAt).TotalSeconds);
                OsoujiTelemetry.OutboxPublishLagSeconds.Record(publishLagSeconds);
            }
            catch (Exception ex)
            {
                var nextAttempt = row.AttemptCount + 1;
                var nextAvailableAt = DateTimeOffset.UtcNow.AddMinutes(Math.Min(30, Math.Max(1, nextAttempt * 5)));
                await connection.ExecuteAsync(
                    """
                    UPDATE outbox_messages
                    SET attempt_count = attempt_count + 1,
                        last_error = @lastError,
                        available_at = @nextAvailableAt
                    WHERE message_id = @messageId;
                    """,
                    new
                    {
                        messageId = row.MessageId,
                        lastError = ex.Message,
                        nextAvailableAt
                    });

                logger.LogWarning(ex, "Outbox publish failed for message {MessageId}", row.MessageId);
            }
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
}

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OsoujiSystem.Infrastructure.Observability;

public static class OsoujiTelemetry
{
    public const string MeterName = "OsoujiSystem.Infrastructure";
    public const string ActivitySourceName = "OsoujiSystem.Infrastructure";

    private static Func<IEnumerable<Measurement<double>>> _projectionLagProvider = static () => [];
    private static Func<IEnumerable<Measurement<long>>> _outboxPendingProvider = static () => [];

    public static readonly Meter Meter = new(MeterName);
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static readonly Counter<long> RabbitConsumerMessagesTotal =
        Meter.CreateCounter<long>("osouji_rabbitmq_consumer_messages_total");

    public static readonly Counter<long> RabbitConsumerFailuresTotal =
        Meter.CreateCounter<long>("osouji_rabbitmq_consumer_failures_total");

    public static readonly Counter<long> RabbitMqDlqMessagesTotal =
        Meter.CreateCounter<long>("osouji_rabbitmq_dlq_messages_total");

    public static readonly Histogram<double> OutboxPublishLagSeconds =
        Meter.CreateHistogram<double>("osouji_outbox_publish_lag_seconds", unit: "s");

    public static readonly Counter<long> HttpRequestsTotal =
        Meter.CreateCounter<long>("osouji_http_requests_total");

    public static readonly Histogram<double> HttpRequestDurationSeconds =
        Meter.CreateHistogram<double>("osouji_http_request_duration_seconds", unit: "s");

    public static readonly ObservableGauge<double> ProjectionCheckpointLagSeconds =
        Meter.CreateObservableGauge("osouji_projection_checkpoint_lag_seconds",
            () => _projectionLagProvider());

    public static readonly ObservableGauge<long> OutboxPendingMessages =
        Meter.CreateObservableGauge("osouji_outbox_pending_messages",
            () => _outboxPendingProvider());

    public static void SetProjectionLagProvider(Func<IEnumerable<Measurement<double>>> provider)
    {
        _projectionLagProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public static void SetOutboxPendingProvider(Func<IEnumerable<Measurement<long>>> provider)
    {
        _outboxPendingProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }
}

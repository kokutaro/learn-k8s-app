using System.Diagnostics;
using AwesomeAssertions;
using OsoujiSystem.Infrastructure.Messaging;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class MessagingTests
{
    [Theory]
    [InlineData(1, "notification.retry.1m", false)]
    [InlineData(2, "notification.retry.5m", false)]
    [InlineData(3, "notification.retry.5m", false)]
    [InlineData(4, "notification.retry.30m", false)]
    [InlineData(5, "notification.dlq", true)]
    [InlineData(10, "notification.dlq", true)]
    public void RetryPolicy_ShouldResolveExpectedDestination(int nextRetryCount, string expectedRoutingKey, bool expectedDlq)
    {
        var destination = RabbitMqRetryPolicy.Resolve(RabbitMqTopology.NotificationConsumer, nextRetryCount);

        destination.RoutingKey.Should().Be(expectedRoutingKey);
        destination.IsDlq.Should().Be(expectedDlq);
        destination.Exchange.Should().Be(expectedDlq ? RabbitMqTopology.DlqExchange : RabbitMqTopology.RetryExchange);
    }

    [Fact]
    public void TryReadEventId_ShouldParseGuidStringHeader()
    {
        var eventId = Guid.NewGuid();
        var headers = new Dictionary<string, object?>
        {
            ["event_id"] = eventId.ToString("D")
        };

        RabbitMqConsumerWorkerBase<INotificationRabbitMqMessageHandler>.TryReadEventId(headers, out var parsed).Should().BeTrue();
        parsed.Should().Be(eventId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void ReadRetryCount_ShouldParseIntHeader(int expected)
    {
        var headers = new Dictionary<string, object?>
        {
            ["x-retry-count"] = expected
        };

        RabbitMqConsumerWorkerBase<INotificationRabbitMqMessageHandler>.ReadRetryCount(headers).Should().Be(expected);
    }

    [Fact]
    public void Inject_ShouldWriteW3CTraceHeaders()
    {
        using var activity = new Activity("http.request");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        var headers = new Dictionary<string, object?>();

        RabbitMqTraceContext.Inject(activity, headers);

        headers[RabbitMqTraceContext.TraceParentHeader].Should().Be(activity.Id);
        headers[RabbitMqTraceContext.TraceIdHeader].Should().Be(activity.TraceId.ToString());
        headers[RabbitMqTraceContext.CorrelationIdHeader].Should().Be(activity.TraceId.ToString());
    }

    [Fact]
    public void TryExtractParentContext_ShouldParseInjectedTraceHeaders()
    {
        using var activity = new Activity("http.request");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        var headers = new Dictionary<string, object?>();
        RabbitMqTraceContext.Inject(activity, headers);

        RabbitMqTraceContext.TryExtractParentContext(headers, out var parentContext).Should().BeTrue();
        parentContext.TraceId.Should().Be(activity.TraceId);
        parentContext.SpanId.Should().Be(activity.SpanId);
        parentContext.IsRemote.Should().BeTrue();
    }

    [Fact]
    public void DeserializePersistedHeaders_ShouldKeepRetryCountNumeric()
    {
        const string serializedHeaders = """
                                         {
                                           "event_id": "11111111-1111-1111-1111-111111111111",
                                           "x-retry-count": 2,
                                           "traceparent": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
                                         }
                                         """;

        var headers = RabbitMqTraceContext.DeserializePersistedHeaders(serializedHeaders);

        headers["x-retry-count"].Should().Be(2);
        headers["traceparent"].Should().Be("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
    }

    [Fact]
    public void TryReadAggregateVersion_ShouldParseLongHeader()
    {
        var headers = new Dictionary<string, object?>
        {
            ["aggregate_version"] = 7L
        };

        UserRegistryIntegrationRabbitMqMessageHandler.TryReadAggregateVersion(headers, out var aggregateVersion)
            .Should().BeTrue();
        aggregateVersion.Should().Be(7);
    }
}

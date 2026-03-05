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

        RabbitMqConsumerWorkerBase.TryReadEventId(headers, out var parsed).Should().BeTrue();
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

        RabbitMqConsumerWorkerBase.ReadRetryCount(headers).Should().Be(expected);
    }
}

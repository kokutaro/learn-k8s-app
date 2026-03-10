using AwesomeAssertions;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Infrastructure.Persistence.Postgres;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class NpgsqlApplicationTransactionTests
{
    [Fact]
    public void PublishConsistencyTokenIfAvailable_ShouldSetTokenFromMaxGlobalPosition()
    {
        var eventWriteContextAccessor = new FakeEventWriteContextAccessor(maxGlobalPosition: 27);
        var consistencyContextAccessor = new FakeReadModelConsistencyContextAccessor();

        NpgsqlApplicationTransaction.PublishConsistencyTokenIfAvailable(
            eventWriteContextAccessor,
            consistencyContextAccessor);

        consistencyContextAccessor.TryGet(out var token).Should().BeTrue();
        token.RequiredGlobalPosition.Should().Be(27);
    }

    [Fact]
    public void PublishConsistencyTokenIfAvailable_ShouldLeaveAccessorEmpty_WhenNoEventsWereWritten()
    {
        var eventWriteContextAccessor = new FakeEventWriteContextAccessor(maxGlobalPosition: null);
        var consistencyContextAccessor = new FakeReadModelConsistencyContextAccessor();

        NpgsqlApplicationTransaction.PublishConsistencyTokenIfAvailable(
            eventWriteContextAccessor,
            consistencyContextAccessor);

        consistencyContextAccessor.TryGet(out _).Should().BeFalse();
    }

    private sealed class FakeEventWriteContextAccessor(long? maxGlobalPosition) : IEventWriteContextAccessor
    {
        public void Initialize()
        {
        }

        public void Register(Domain.Abstractions.IDomainEvent domainEvent, Guid eventId, long streamVersion, long globalPosition)
        {
        }

        public bool TryGetMetadata(Domain.Abstractions.IDomainEvent domainEvent, out EventWriteMetadata metadata)
        {
            metadata = default;
            return false;
        }

        public bool TryGetMaxGlobalPosition(out long globalPosition)
        {
            if (maxGlobalPosition is { } value)
            {
                globalPosition = value;
                return true;
            }

            globalPosition = default;
            return false;
        }

        public void Clear()
        {
        }
    }

    private sealed class FakeReadModelConsistencyContextAccessor : IReadModelConsistencyContextAccessor
    {
        private ReadModelConsistencyToken? _token;

        public bool TryGet(out ReadModelConsistencyToken token)
        {
            if (_token is { } value)
            {
                token = value;
                return true;
            }

            token = default;
            return false;
        }

        public void Set(ReadModelConsistencyToken token) => _token = token;

        public void Clear() => _token = null;
    }
}

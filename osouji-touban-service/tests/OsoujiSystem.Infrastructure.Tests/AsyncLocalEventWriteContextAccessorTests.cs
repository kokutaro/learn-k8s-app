using AwesomeAssertions;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Domain.ValueObjects;
using OsoujiSystem.Infrastructure.Persistence.Postgres;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class AsyncLocalEventWriteContextAccessorTests
{
    [Fact]
    public async Task Register_ShouldRemainVisibleAfterAwait_WhenContextInitializedBeforeAwait()
    {
        var accessor = new AsyncLocalEventWriteContextAccessor();
        var weekRule = new WeekRule(DayOfWeek.Monday, new TimeOnly(9, 0), "Asia/Tokyo", new WeekId(2026, 10));
        var domainEvent = new CleaningAreaRegistered(
            new OsoujiSystem.Domain.Entities.CleaningAreas.CleaningAreaId(Guid.NewGuid()),
            "Area A",
            weekRule);
        var eventId = Guid.NewGuid();

        accessor.Initialize();

        await RegisterAsync(accessor, domainEvent, eventId, 3, 42);

        accessor.TryGetMetadata(domainEvent, out var actual).Should().BeTrue();
        actual.EventId.Should().Be(eventId);
        actual.StreamVersion.Should().Be(3);
        actual.GlobalPosition.Should().Be(42);
        accessor.TryGetMaxGlobalPosition(out var maxGlobalPosition).Should().BeTrue();
        maxGlobalPosition.Should().Be(42);
    }

    [Fact]
    public async Task Register_ShouldNotFlowBackAfterAwait_WhenContextInitializedInsideAwaitedMethod()
    {
        var accessor = new AsyncLocalEventWriteContextAccessor();
        var weekRule = new WeekRule(DayOfWeek.Tuesday, new TimeOnly(10, 0), "Asia/Tokyo", new WeekId(2026, 11));
        var domainEvent = new CleaningAreaRegistered(
            new OsoujiSystem.Domain.Entities.CleaningAreas.CleaningAreaId(Guid.NewGuid()),
            "Area B",
            weekRule);

        await RegisterAsync(accessor, domainEvent, Guid.NewGuid(), 2, 21);

        accessor.TryGetMetadata(domainEvent, out _).Should().BeFalse();
        accessor.TryGetMaxGlobalPosition(out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetMaxGlobalPosition_ShouldReturnGreatestRegisteredPosition()
    {
        var accessor = new AsyncLocalEventWriteContextAccessor();
        accessor.Initialize();

        var firstEvent = CreateRegisteredEvent(DayOfWeek.Wednesday, 12);
        var secondEvent = CreateRegisteredEvent(DayOfWeek.Thursday, 13);

        accessor.Register(firstEvent, Guid.NewGuid(), 1, 5);
        accessor.Register(secondEvent, Guid.NewGuid(), 2, 9);

        accessor.TryGetMaxGlobalPosition(out var maxGlobalPosition).Should().BeTrue();
        maxGlobalPosition.Should().Be(9);
    }

    private static async Task RegisterAsync(
        AsyncLocalEventWriteContextAccessor accessor,
        CleaningAreaRegistered domainEvent,
        Guid eventId,
        long streamVersion,
        long globalPosition)
    {
        await Task.Yield();
        accessor.Register(domainEvent, eventId, streamVersion, globalPosition);
    }

    private static CleaningAreaRegistered CreateRegisteredEvent(DayOfWeek startDay, int week)
    {
        var weekRule = new WeekRule(startDay, new TimeOnly(9, 0), "Asia/Tokyo", new WeekId(2026, week));
        return new CleaningAreaRegistered(
            new OsoujiSystem.Domain.Entities.CleaningAreas.CleaningAreaId(Guid.NewGuid()),
            $"Area-{week}",
            weekRule);
    }
}

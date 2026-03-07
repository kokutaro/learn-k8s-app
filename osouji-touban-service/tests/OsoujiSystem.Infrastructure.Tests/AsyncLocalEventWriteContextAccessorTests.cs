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

        await RegisterAsync(accessor, domainEvent, eventId);

        accessor.TryGetEventId(domainEvent, out var actual).Should().BeTrue();
        actual.Should().Be(eventId);
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

        await RegisterAsync(accessor, domainEvent, Guid.NewGuid());

        accessor.TryGetEventId(domainEvent, out _).Should().BeFalse();
    }

    private static async Task RegisterAsync(
        AsyncLocalEventWriteContextAccessor accessor,
        CleaningAreaRegistered domainEvent,
        Guid eventId)
    {
        await Task.Yield();
        accessor.Register(domainEvent, eventId);
    }
}

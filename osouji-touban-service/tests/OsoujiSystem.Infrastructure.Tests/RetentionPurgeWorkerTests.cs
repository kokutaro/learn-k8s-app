using AwesomeAssertions;
using OsoujiSystem.Infrastructure.Retention;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class RetentionPurgeWorkerTests
{
    [Fact]
    public void ParseDailyRunTime_ShouldFallback_WhenInvalid()
    {
        var parsed = RetentionPurgeWorker.ParseDailyRunTime("bad");

        parsed.Should().Be(new TimeSpan(3, 30, 0));
    }

    [Fact]
    public void ComputeNextRunUtc_ShouldReturnSameDay_WhenBeforeRunTime()
    {
        var nowUtc = new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero); // JST 09:00

        var nextRun = RetentionPurgeWorker.ComputeNextRunUtc(nowUtc, "12:00"); // JST noon

        nextRun.Should().Be(new DateTimeOffset(2026, 3, 5, 3, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ComputeNextRunUtc_ShouldReturnNextDay_WhenPastRunTime()
    {
        var nowUtc = new DateTimeOffset(2026, 3, 5, 12, 0, 0, TimeSpan.Zero); // JST 21:00

        var nextRun = RetentionPurgeWorker.ComputeNextRunUtc(nowUtc, "03:30");

        nextRun.Should().Be(new DateTimeOffset(2026, 3, 5, 18, 30, 0, TimeSpan.Zero));
    }
}

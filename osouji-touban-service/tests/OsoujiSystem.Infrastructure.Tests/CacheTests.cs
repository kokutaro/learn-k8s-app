using System.Text.Json;
using AwesomeAssertions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;
using OsoujiSystem.Infrastructure.Cache;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class CacheTests
{
    [Fact]
    public void CacheKeyFactory_ShouldGenerateExpectedKeys()
    {
        var factory = new CacheKeyFactory();
        var areaId = new CleaningAreaId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var planId = new WeeklyDutyPlanId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var userId = new UserId(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var weekId = new WeekId(2026, 12);

        factory.WeeklyPlanVersion(planId, 5).Should().Be("weekly-plan:22222222-2222-2222-2222-222222222222:v5");
        factory.WeeklyPlanLatest(planId).Should().Be("weekly-plan:22222222-2222-2222-2222-222222222222:latest");
        factory.WeeklyPlanAreaWeekLatest(areaId, weekId).Should().Be("weekly-plan:11111111-1111-1111-1111-111111111111:2026-12:latest");
        factory.CleaningAreaVersion(areaId, 3).Should().Be("cleaning-area:11111111-1111-1111-1111-111111111111:v3");
        factory.CleaningAreaLatest(areaId).Should().Be("cleaning-area:11111111-1111-1111-1111-111111111111:latest");
        factory.CleaningAreaUserLatest(userId).Should().Be("cleaning-area:user:33333333-3333-3333-3333-333333333333:latest");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 60)]
    [InlineData(5, 360)]
    [InlineData(99, 360)]
    public void ComputeBackoff_ShouldMatchPolicy(int retryCount, int expectedMinutes)
    {
        CacheInvalidationRecoveryWorker.ComputeBackoff(retryCount)
            .Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Fact]
    public void CacheEnvelope_ShouldRoundTripJson()
    {
        var source = new CacheEnvelope(7, "{\"foo\":\"bar\"}", DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(source);
        var restored = JsonSerializer.Deserialize<CacheEnvelope>(json);

        restored.Should().NotBeNull();
        restored.Version.Should().Be(7);
        restored.Payload.Should().Be("{\"foo\":\"bar\"}");
    }
}

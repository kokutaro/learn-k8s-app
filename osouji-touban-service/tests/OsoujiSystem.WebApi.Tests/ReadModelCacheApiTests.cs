using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace OsoujiSystem.WebApi.Tests;

[Collection(ApiIntegrationTestCollection.Name)]
public sealed class ReadModelCacheApiTests(ApiIntegrationTestFixture fixture) : IAsyncLifetime
{
    private HttpClient _client = null!;
    private IConnectionMultiplexer _redis = null!;

    public async ValueTask InitializeAsync()
    {
        _client = fixture.CreateClient();
        _redis = fixture.Factory.Services.GetRequiredService<IConnectionMultiplexer>();
        await fixture.ResetAsync();
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task GetCleaningArea_ShouldPopulateReadModelCacheKeys()
    {
        var areaId = Guid.NewGuid();
        await RegisterAreaAsync(areaId);

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
        var response = await _client.GetAsync($"/api/v1/cleaning-areas/{areaId}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var database = _redis.GetDatabase();
        var latestKey = $"readmodel:cleaning-area:{areaId:D}:latest";
        var latestVersion = await database.StringGetAsync(latestKey);

        latestVersion.HasValue.Should().BeTrue();
        var detailKey = $"readmodel:cleaning-area:{areaId:D}:v{latestVersion}";
        (await database.KeyExistsAsync(detailKey)).Should().BeTrue();
    }

    [Fact]
    public async Task GetWeeklyDutyPlan_ShouldPopulateReadModelCacheKeys()
    {
        var areaId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await RegisterAreaWithMemberAsync(areaId, userId);

        var createResponse = await _client.PostAsJsonAsync("/api/v1/weekly-duty-plans", new
        {
            areaId,
            weekId = "2026-W10",
            policy = new
            {
                fairnessWindowWeeks = 4
            }
        }, TestContext.Current.CancellationToken);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        var planId = Guid.Parse(createBody!["data"]!["planId"]!.GetValue<string>());

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
        var response = await _client.GetAsync($"/api/v1/weekly-duty-plans/{planId}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var database = _redis.GetDatabase();
        var latestKey = $"readmodel:weekly-plan:{planId:D}:latest";
        var latestVersion = await database.StringGetAsync(latestKey);

        latestVersion.HasValue.Should().BeTrue();
        var detailKey = $"readmodel:weekly-plan:{planId:D}:v{latestVersion}";
        (await database.KeyExistsAsync(detailKey)).Should().BeTrue();
    }

    private async Task RegisterAreaAsync(Guid areaId)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/cleaning-areas", new
        {
            facilityId = ApiTestHelper.LegacyFacilityId,
            areaId,
            name = "Cache Test Area",
            initialWeekRule = new
            {
                startDay = "monday",
                startTime = "09:00:00",
                timeZoneId = "Asia/Tokyo",
                effectiveFromWeek = "2026-W10"
            },
            initialSpots = new[]
            {
                new
                {
                    spotId = Guid.NewGuid(),
                    spotName = "Hallway",
                    sortOrder = 10
                }
            }
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task RegisterAreaWithMemberAsync(Guid areaId, Guid userId)
    {
        await RegisterAreaAsync(areaId);

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
        var areaGet = await _client.GetAsync($"/api/v1/cleaning-areas/{areaId}", TestContext.Current.CancellationToken);
        areaGet.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = areaGet.Headers.ETag!.Tag;

        using var assignRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cleaning-areas/{areaId}/members");
        assignRequest.Headers.TryAddWithoutValidation("If-Match", etag);
        assignRequest.Content = JsonContent.Create(new
        {
            userId,
            employeeNumber = "123456"
        });

        var assignResponse = await _client.SendAsync(assignRequest, TestContext.Current.CancellationToken);
        assignResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Infrastructure.Options;

namespace OsoujiSystem.WebApi.Tests;

[Collection(ApiIntegrationTestCollection.Name)]
public sealed class ReadModelVisibilityApiTests(ApiIntegrationTestFixture fixture) : IAsyncLifetime
{
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _client = CreateTimeoutClient();
        await fixture.ResetAsync();
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task RegisterFacility_WhenWaiterTimesOut_ShouldReturnAccepted_AndGetShouldSucceedAfterProjectionDrain()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/facilities", new
        {
            facilityCode = "TOKYO-PENDING",
            name = "Tokyo Pending",
            description = "Pending projection",
            timeZoneId = "Asia/Tokyo"
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.GetValues("Retry-After").Single().Should().Be("1");
        response.Headers.GetValues("X-ReadModel-Visibility").Single().Should().Be("pending");

        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        var facilityId = Guid.Parse(body!["data"]!["resourceId"]!.GetValue<string>());
        var location = body["data"]!["location"]!.GetValue<string>();

        var beforeDrain = await _client.GetAsync(location, TestContext.Current.CancellationToken);
        beforeDrain.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);

        var afterDrain = await _client.GetAsync(location, TestContext.Current.CancellationToken);
        afterDrain.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterBody = await afterDrain.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        afterBody!["data"]!["id"]!.GetValue<string>().Should().Be(facilityId.ToString("D"));
        afterBody["data"]!["facilityCode"]!.GetValue<string>().Should().Be("TOKYO-PENDING");
    }

    [Fact]
    public async Task RemoveCleaningSpot_WhenWaiterTimesOut_ShouldReturnAccepted_AndAreaShouldReflectDeletionAfterProjectionDrain()
    {
        var areaId = Guid.NewGuid();
        var spotId = Guid.NewGuid();
        using var setupClient = fixture.CreateClient();
        await ApiTestHelper.RegisterAreaAsync(setupClient, areaId, "Pending Delete", (spotId, "Lobby", 10), (Guid.NewGuid(), "Pantry", 20));

        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, setupClient, areaId);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cleaning-areas/{areaId}/spots/{spotId}");
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location!.ToString().Should().Be($"/api/v1/cleaning-areas/{areaId}");
        response.Headers.GetValues("X-ReadModel-Visibility").Single().Should().Be("pending");

        var beforeDrain = await _client.GetAsync($"/api/v1/cleaning-areas/{areaId}", TestContext.Current.CancellationToken);
        beforeDrain.StatusCode.Should().Be(HttpStatusCode.OK);
        var beforeBody = await beforeDrain.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        beforeBody!["data"]!["spots"]!.AsArray().Should().Contain(node => node!["id"]!.GetValue<string>() == spotId.ToString());

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);

        var afterDrain = await _client.GetAsync($"/api/v1/cleaning-areas/{areaId}", TestContext.Current.CancellationToken);
        afterDrain.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterBody = await afterDrain.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        afterBody!["data"]!["spots"]!.AsArray().Should().NotContain(node => node!["id"]!.GetValue<string>() == spotId.ToString());
    }

    private HttpClient CreateTimeoutClient()
        => fixture.CreateClient(
            new Dictionary<string, string?>
            {
                ["Infrastructure:ProjectionVisibility:Enabled"] = "true"
            },
            services =>
            {
                services.RemoveAll<IOptions<InfrastructureOptions>>();
                services.AddSingleton(
                    Options.Create(new InfrastructureOptions
                    {
                        PersistenceMode = "EventStore",
                        ProjectionVisibility = new ProjectionVisibilityOptions
                        {
                            Enabled = true,
                            WaitTimeoutMs = 20,
                            PollIntervalMs = 5
                        }
                    }));
                services.RemoveAll<IReadModelVisibilityWaiter>();
                services.AddSingleton<IReadModelVisibilityWaiter>(new TimeoutReadModelVisibilityWaiter());
            });

    private sealed class TimeoutReadModelVisibilityWaiter : IReadModelVisibilityWaiter
    {
        public Task<ReadModelVisibilityWaitResult> WaitUntilVisibleAsync(ReadModelConsistencyToken token, CancellationToken ct)
            => Task.FromResult(new ReadModelVisibilityWaitResult(
                IsVisible: false,
                TimedOut: true,
                Waited: TimeSpan.FromMilliseconds(25)));
    }
}

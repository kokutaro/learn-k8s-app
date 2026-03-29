using System.Net.Http.Headers;

namespace OsoujiSystem.WebApi.Tests;

[Collection(ApiIntegrationTestCollection.Name)]
public sealed class FacilityApiTests(ApiIntegrationTestFixture fixture) : IAsyncLifetime
{
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _client = fixture.CreateClient();
        await fixture.ResetAsync();
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task RegisterFacility_ThenGet_ShouldReturnCreatedResourceAndEtag()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/facilities", new
        {
            facilityCode = "TOKYO-HQ",
            name = "Tokyo HQ",
            description = "Main office",
            timeZoneId = "Asia/Tokyo"
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        var facilityId = Guid.Parse(body!["data"]!["facilityId"]!.GetValue<string>());

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
        var getResponse = await _client.GetAsync($"/api/v1/facilities/{facilityId}", TestContext.Current.CancellationToken);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.Headers.ETag!.Tag.Should().Be("\"1\"");

        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        getBody!["data"]!["facilityCode"]!.GetValue<string>().Should().Be("TOKYO-HQ");
        getBody["data"]!["name"]!.GetValue<string>().Should().Be("Tokyo HQ");
        getBody["data"]!["lifecycleStatus"]!.GetValue<string>().Should().Be("active");
    }

    [Fact]
    public async Task UpdateFacility_WithIfMatch_ShouldAdvanceVersion()
    {
        var facilityId = await RegisterFacilityAsync("TOKYO-HQ");
        var etag = await GetFacilityEtagAsync(facilityId);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/facilities/{facilityId}");
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
        request.Content = JsonContent.Create(new
        {
            name = "Tokyo HQ Annex",
            description = "Annex building",
            timeZoneId = "Asia/Tokyo"
        });

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.Should().Be("\"2\"");
    }

    [Fact]
    public async Task ChangeFacilityActivation_ThenRegisterCleaningArea_ShouldRejectInactiveFacility()
    {
        var facilityId = await RegisterFacilityAsync("TOKYO-BRANCH");
        var etag = await GetFacilityEtagAsync(facilityId);

        using var deactivate = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/facilities/{facilityId}/activation");
        deactivate.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
        deactivate.Content = JsonContent.Create(new
        {
            lifecycleStatus = "inactive"
        });

        var deactivateResponse = await _client.SendAsync(deactivate, TestContext.Current.CancellationToken);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);

        var areaResponse = await _client.PostAsJsonAsync("/api/v1/cleaning-areas", new
        {
            facilityId,
            areaId = Guid.NewGuid(),
            name = "Blocked Area",
            initialWeekRule = new
            {
                startDay = "monday",
                startTime = "09:00:00",
                timeZoneId = "Asia/Tokyo",
                effectiveFromWeek = ApiTestHelper.CurrentWeek
            },
            initialSpots = new[]
            {
                new
                {
                    spotId = Guid.NewGuid(),
                    spotName = "Pantry",
                    sortOrder = 10
                }
            }
        }, TestContext.Current.CancellationToken);

        areaResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await areaResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["error"]!["code"]!.GetValue<string>().Should().Be("FacilityNotActiveError");
    }

    private async Task<Guid> RegisterFacilityAsync(string facilityCode)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/facilities", new
        {
            facilityCode,
            name = facilityCode,
            description = "Facility",
            timeZoneId = "Asia/Tokyo"
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        return Guid.Parse(body!["data"]!["facilityId"]!.GetValue<string>());
    }

    private async Task<string> GetFacilityEtagAsync(Guid facilityId)
    {
        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
        var response = await _client.GetAsync($"/api/v1/facilities/{facilityId}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return response.Headers.ETag!.Tag;
    }
}

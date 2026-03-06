namespace OsoujiSystem.WebApi.Tests;

[Collection(ApiIntegrationTestCollection.Name)]
public sealed class CleaningAreaApiTests(ApiIntegrationTestFixture fixture) : IAsyncLifetime
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
    public async Task RegisterCleaningArea_ThenGet_ShouldReturnCreatedResourceAndEtag()
    {
        var areaId = Guid.NewGuid();
        var spotId = Guid.NewGuid();

        var createResponse = await _client.PostAsJsonAsync("/api/v1/cleaning-areas", new
        {
            areaId,
            name = "3F East",
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
                    spotId,
                    spotName = "Pantry",
                    sortOrder = 10
                }
            }
        }, TestContext.Current.CancellationToken);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        createResponse.Headers.Location.Should().NotBeNull();
        createResponse.Headers.Location!.ToString().Should().Be($"/api/v1/cleaning-areas/{areaId}");

        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        createBody!["data"]!["areaId"]!.GetValue<string>().Should().Be(areaId.ToString());

        var getResponse = await _client.GetAsync($"/api/v1/cleaning-areas/{areaId}", TestContext.Current.CancellationToken);
        var getBodyText = await getResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, getBodyText);
        getResponse.Headers.ETag.Should().NotBeNull();
        getResponse.Headers.ETag!.Tag.Should().Be("\"1\"");

        var body = JsonNode.Parse(getBodyText)!.AsObject();
        body["data"]!["id"]!.GetValue<string>().Should().Be(areaId.ToString());
        body["data"]!["name"]!.GetValue<string>().Should().Be("3F East");
        body["data"]!["version"]!.GetValue<long>().Should().Be(1);
        body["data"]!["spots"]!.AsArray().Should().HaveCount(1);
        body["data"]!["spots"]![0]!["id"]!.GetValue<string>().Should().Be(spotId.ToString());
    }

    [Fact]
    public async Task RegisterCleaningArea_WithInvalidPayload_ShouldReturnValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/cleaning-areas", new
        {
            areaId = "not-a-guid",
            name = "Invalid",
            initialWeekRule = new
            {
                startDay = "noday",
                startTime = "25:00:00",
                timeZoneId = "",
                effectiveFromWeek = "2026-W99"
            },
            initialSpots = Array.Empty<object>()
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["errors"]!["areaId"]!.AsArray().Should().ContainSingle();
        body["errors"]!["initialWeekRule.startDay"]!.AsArray().Should().ContainSingle();
        body["errors"]!["initialWeekRule.startTime"]!.AsArray().Should().ContainSingle();
        body["errors"]!["initialWeekRule.effectiveFromWeek"]!.AsArray().Should().ContainSingle();
        body["errors"]!["initialSpots"]!.AsArray().Should().ContainSingle();
    }

    [Fact]
    public async Task AssignUserToArea_WithMatchingIfMatch_ShouldCreateMemberAndAdvanceVersion()
    {
        var areaId = await RegisterAreaAsync();
        var getResponse = await _client.GetAsync($"/api/v1/cleaning-areas/{areaId}", TestContext.Current.CancellationToken);
        var getBodyText = await getResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, getBodyText);
        var etag = getResponse.Headers.ETag!.Tag;
        var userId = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cleaning-areas/{areaId}/members");
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content = JsonContent.Create(new
        {
            userId,
            employeeNumber = "123456"
        });

        var assignResponse = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        assignResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        assignResponse.Headers.Location!.ToString().Should().Be($"/api/v1/cleaning-areas/{areaId}/members/{userId}");
        assignResponse.Headers.ETag!.Tag.Should().Be("\"2\"");

        var body = await assignResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["data"]!["userId"]!.GetValue<string>().Should().Be(userId.ToString());
        body["data"]!["memberId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();

        var refreshed = await _client.GetFromJsonAsync<JsonObject>($"/api/v1/cleaning-areas/{areaId}", TestContext.Current.CancellationToken);
        refreshed!["data"]!["members"]!.AsArray().Should().HaveCount(1);
        refreshed["data"]!["members"]![0]!["userId"]!.GetValue<string>().Should().Be(userId.ToString());
        refreshed["data"]!["version"]!.GetValue<long>().Should().Be(2);
    }

    [Fact]
    public async Task AssignUserToArea_WithoutIfMatch_ShouldReturnValidationProblem()
    {
        var areaId = await RegisterAreaAsync();

        var response = await _client.PostAsJsonAsync($"/api/v1/cleaning-areas/{areaId}/members", new
        {
            userId = Guid.NewGuid(),
            employeeNumber = "123456"
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["errors"]!["If-Match"]!.AsArray().Should().ContainSingle();
    }

    private async Task<Guid> RegisterAreaAsync()
    {
        var areaId = Guid.NewGuid();
        var response = await _client.PostAsJsonAsync("/api/v1/cleaning-areas", new
        {
            areaId,
            name = "Main Area",
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
                    spotName = "Sink",
                    sortOrder = 10
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return areaId;
    }
}

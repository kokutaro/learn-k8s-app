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
            facilityId = ApiTestHelper.LegacyFacilityId,
            areaId,
            name = "3F East",
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
                    spotId,
                    spotName = "Pantry",
                    sortOrder = 10
                }
            }
        }, TestContext.Current.CancellationToken);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        createResponse.Headers.Location!.ToString().Should().Be($"/api/v1/cleaning-areas/{areaId}");

        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        createBody!["data"]!["areaId"]!.GetValue<string>().Should().Be(areaId.ToString());

        var getBody = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);

        etag.Should().Be("\"1\"");
        getBody["data"]!["id"]!.GetValue<string>().Should().Be(areaId.ToString());
        getBody["data"]!["facilityId"]!.GetValue<string>().Should().Be(ApiTestHelper.LegacyFacilityId.ToString());
        getBody["data"]!["name"]!.GetValue<string>().Should().Be("3F East");
        getBody["data"]!["version"]!.GetValue<long>().Should().Be(1);
        getBody["data"]!["spots"]!.AsArray().Should().HaveCount(1);
        getBody["data"]!["spots"]![0]!["id"]!.GetValue<string>().Should().Be(spotId.ToString());
    }

    [Fact]
    public async Task RegisterCleaningArea_WithInvalidPayload_ShouldReturnValidationError()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/cleaning-areas", new
        {
            facilityId = "not-a-guid",
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
        body!["error"]!["code"]!.GetValue<string>().Should().Be("ValidationError");
        var details = body["error"]!["details"]!.AsArray();
        var fields = details
            .Select(detail => detail!["field"]!.GetValue<string>())
            .ToArray();
        fields.Should().Contain("facilityId");
        fields.Should().Contain("areaId");
        fields.Should().Contain("initialWeekRule.startDay");
        fields.Should().Contain("initialWeekRule.startTime");
        fields.Should().Contain("initialWeekRule.effectiveFromWeek");
        fields.Should().Contain("initialSpots");
    }

    [Fact]
    public async Task AssignUserToArea_WithMatchingIfMatch_ShouldCreateMemberAndAdvanceVersion()
    {
        var areaId = Guid.NewGuid();
        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));

        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        var userId = Guid.NewGuid();

        var assignResponse = await ApiTestHelper.AssignUserAsync(_client, areaId, userId, etag, "000001");

        assignResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        assignResponse.Headers.Location!.ToString().Should().Be($"/api/v1/cleaning-areas/{areaId}/members/{userId}");
        assignResponse.Headers.ETag!.Tag.Should().Be("\"2\"");

        var body = await assignResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["data"]!["userId"]!.GetValue<string>().Should().Be(userId.ToString());
        body["data"]!["memberId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();

        var refreshed = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        refreshed["data"]!["members"]!.AsArray().Should().HaveCount(1);
        refreshed["data"]!["members"]![0]!["userId"]!.GetValue<string>().Should().Be(userId.ToString());
        refreshed["data"]!["members"]![0]!["employeeNumber"]!.GetValue<string>().Should().Be("000001");
        refreshed["data"]!["version"]!.GetValue<long>().Should().Be(2);
    }

    [Fact]
    public async Task AssignUserToArea_WithoutIfMatch_ShouldReturnValidationError()
    {
        var areaId = Guid.NewGuid();
        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));

        var response = await _client.PostAsJsonAsync($"/api/v1/cleaning-areas/{areaId}/members", new
        {
            userId = Guid.NewGuid(),
            employeeNumber = "000001"
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["error"]!["code"]!.GetValue<string>().Should().Be("ValidationError");
        body["error"]!["details"]!.AsArray()
            .Select(detail => detail!["field"]!.GetValue<string>())
            .Should().Contain("If-Match");
    }

    [Fact]
    public async Task ScheduleWeekRuleChange_ShouldStorePendingRuleAndAdvanceVersion()
    {
        var areaId = Guid.NewGuid();
        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "HQ", (Guid.NewGuid(), "Lobby", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/cleaning-areas/{areaId}/pending-week-rule");
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content = JsonContent.Create(new
        {
            startDay = "tuesday",
            startTime = "08:30:00",
            timeZoneId = "Asia/Tokyo",
            effectiveFromWeek = ApiTestHelper.FutureWeek
        });

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.Should().Be("\"2\"");

        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["data"]!["currentWeekRule"]!["startDay"]!.GetValue<string>().Should().Be("monday");
        body["data"]!["pendingWeekRule"]!["startDay"]!.GetValue<string>().Should().Be("tuesday");
        body["data"]!["pendingWeekRule"]!["effectiveFromWeek"]!.GetValue<string>().Should().Be(ApiTestHelper.FutureWeek);

        var detail = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        detail["data"]!["pendingWeekRule"]!["effectiveFromWeek"]!.GetValue<string>().Should().Be(ApiTestHelper.FutureWeek);
        detail["data"]!["version"]!.GetValue<long>().Should().Be(2);
    }

    [Fact]
    public async Task GetCurrentWeek_ShouldReturnResolvedWeekForAreaTimeZone()
    {
        var areaId = Guid.NewGuid();
        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "HQ", (Guid.NewGuid(), "Lobby", 10));
        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);

        var response = await _client.GetAsync($"/api/v1/cleaning-areas/{areaId}/current-week", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().NotBeNull();
        body!["data"]!["areaId"]!.GetValue<string>().Should().Be(areaId.ToString());
        body["data"]!["weekId"]!.GetValue<string>().Should().Be(ApiTestHelper.CurrentWeek);
        body["data"]!["timeZoneId"]!.GetValue<string>().Should().Be("Asia/Tokyo");
    }

    [Fact]
    public async Task AddCleaningSpot_DuplicateSpotId_ShouldReturnConflict()
    {
        var areaId = Guid.NewGuid();
        var spotId = Guid.NewGuid();
        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "HQ", (spotId, "Lobby", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cleaning-areas/{areaId}/spots");
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content = JsonContent.Create(new
        {
            spotId,
            name = "Duplicate Lobby",
            sortOrder = 20
        });

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["error"]!["code"]!.GetValue<string>().Should().Be("DuplicateCleaningSpotError");
    }

    [Fact]
    public async Task RemoveCleaningSpot_ShouldReturnNoContentForUnknownSpot_AndConflictForLastSpot()
    {
        var areaId = Guid.NewGuid();
        var existingSpotId = Guid.NewGuid();
        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "HQ", (existingSpotId, "Lobby", 10));

        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        using var unknownSpotRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cleaning-areas/{areaId}/spots/{Guid.NewGuid()}");
        unknownSpotRequest.Headers.TryAddWithoutValidation("If-Match", etag);

        var unknownSpotResponse = await _client.SendAsync(unknownSpotRequest, TestContext.Current.CancellationToken);
        unknownSpotResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshedEtag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        using var lastSpotRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cleaning-areas/{areaId}/spots/{existingSpotId}");
        lastSpotRequest.Headers.TryAddWithoutValidation("If-Match", refreshedEtag);

        var lastSpotResponse = await _client.SendAsync(lastSpotRequest, TestContext.Current.CancellationToken);

        lastSpotResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await lastSpotResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["error"]!["code"]!.GetValue<string>().Should().Be("CleaningAreaHasNoSpotError");
    }

    [Fact]
    public async Task UnassignUserFromArea_WhenUserIsNotAssigned_ShouldReturnNoContent()
    {
        var areaId = Guid.NewGuid();
        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cleaning-areas/{areaId}/members/{Guid.NewGuid()}");
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var detail = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        detail["data"]!["members"]!.AsArray().Should().BeEmpty();
        detail["data"]!["version"]!.GetValue<long>().Should().Be(1);
    }

    [Fact]
    public async Task ListCleaningAreas_ShouldSupportUserFilterSortingAndCursor()
    {
        var sharedUserId = Guid.NewGuid();
        var areaAId = Guid.NewGuid();
        var areaBId = Guid.NewGuid();

        await ApiTestHelper.RegisterAreaAsync(_client, areaAId, "Alpha Area", (Guid.NewGuid(), "Sink", 10));
        await ApiTestHelper.RegisterAreaAsync(_client, areaBId, "Beta Area", (Guid.NewGuid(), "Pantry", 10));

        var areaAEtag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaAId);
        var areaBEtag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaBId);

        (await ApiTestHelper.AssignUserAsync(_client, areaAId, sharedUserId, areaAEtag, "000001")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await ApiTestHelper.AssignUserAsync(_client, areaBId, Guid.NewGuid(), areaBEtag, "000002")).StatusCode.Should().Be(HttpStatusCode.Created);

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);

        var filteredResponse = await _client.GetFromJsonAsync<JsonObject>(
            $"/api/v1/cleaning-areas?userId={sharedUserId}&sort=name&limit=1",
            TestContext.Current.CancellationToken);

        filteredResponse!["data"]!.AsArray().Should().HaveCount(1);
        filteredResponse["data"]![0]!["id"]!.GetValue<string>().Should().Be(areaAId.ToString());
        filteredResponse["meta"]!["hasNext"]!.GetValue<bool>().Should().BeFalse();

        var pagedResponse = await _client.GetFromJsonAsync<JsonObject>(
            "/api/v1/cleaning-areas?sort=name&limit=1",
            TestContext.Current.CancellationToken);

        pagedResponse!["data"]!.AsArray().Should().HaveCount(1);
        pagedResponse["data"]![0]!["name"]!.GetValue<string>().Should().Be("Alpha Area");
        pagedResponse["meta"]!["hasNext"]!.GetValue<bool>().Should().BeTrue();
        pagedResponse["meta"]!["nextCursor"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();

        var nextCursor = pagedResponse["meta"]!["nextCursor"]!.GetValue<string>();
        var secondPage = await _client.GetFromJsonAsync<JsonObject>(
            $"/api/v1/cleaning-areas?sort=name&limit=1&cursor={Uri.EscapeDataString(nextCursor)}",
            TestContext.Current.CancellationToken);

        secondPage!["data"]!.AsArray().Should().HaveCount(1);
        secondPage["data"]![0]!["name"]!.GetValue<string>().Should().Be("Beta Area");
    }
}

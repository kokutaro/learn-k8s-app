using System.Net.Http.Headers;

namespace OsoujiSystem.WebApi.Tests;

[Collection(ApiIntegrationTestCollection.Name)]
public sealed class WeeklyDutyPlanApiTests(ApiIntegrationTestFixture fixture) : IAsyncLifetime
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
    public async Task GenerateAndPublishWeeklyPlan_ShouldReturnCreatedThenPublished()
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
        createBody["data"]!["status"]!.GetValue<string>().Should().Be("draft");
        createBody["data"]!["revision"]!.GetValue<int>().Should().Be(1);

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
        var getResponse = await _client.GetAsync($"/api/v1/weekly-duty-plans/{planId}", TestContext.Current.CancellationToken);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.Headers.ETag!.Tag.Should().Be("\"2\"");

        var detail = await getResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        detail!["data"]!["areaId"]!.GetValue<string>().Should().Be(areaId.ToString());
        detail["data"]!["assignments"]!.AsArray().Should().HaveCount(1);
        detail["data"]!["assignments"]![0]!["userId"]!.GetValue<string>().Should().Be(userId.ToString());

        using var publishRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/weekly-duty-plans/{planId}/publication");
        publishRequest.Headers.TryAddWithoutValidation("If-Match", "\"2\"");
        publishRequest.Content = JsonContent.Create(new { });

        var publishResponse = await _client.SendAsync(publishRequest, TestContext.Current.CancellationToken);
        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        publishResponse.Headers.ETag!.Tag.Should().Be("\"3\"");

        var publishBody = await publishResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        publishBody!["data"]!["planId"]!.GetValue<string>().Should().Be(planId.ToString());
        publishBody["data"]!["status"]!.GetValue<string>().Should().Be("published");
    }

    [Fact]
    public async Task GenerateWeeklyPlan_ForSameAreaAndWeekTwice_ShouldReturnConflict()
    {
        var areaId = Guid.NewGuid();
        await RegisterAreaWithMemberAsync(areaId, Guid.NewGuid());

        var request = new
        {
            areaId,
            weekId = "2026-W10",
            policy = new
            {
                fairnessWindowWeeks = 4
            }
        };

        var first = await _client.PostAsJsonAsync("/api/v1/weekly-duty-plans", request, TestContext.Current.CancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await _client.PostAsJsonAsync("/api/v1/weekly-duty-plans", request, TestContext.Current.CancellationToken);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await second.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["error"]!["code"]!.GetValue<string>().Should().Be("WeeklyPlanAlreadyExists");
    }

    [Fact]
    public async Task PublishWeeklyPlan_WithStaleIfMatch_ShouldReturnConflict()
    {
        var areaId = Guid.NewGuid();
        await RegisterAreaWithMemberAsync(areaId, Guid.NewGuid());

        var createResponse = await _client.PostAsJsonAsync("/api/v1/weekly-duty-plans", new
        {
            areaId,
            weekId = "2026-W10"
        }, TestContext.Current.CancellationToken);
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        var planId = Guid.Parse(createBody!["data"]!["planId"]!.GetValue<string>());

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/weekly-duty-plans/{planId}/publication");
        request.Headers.TryAddWithoutValidation("If-Match", "\"99\"");
        request.Content = JsonContent.Create(new { });

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["error"]!["code"]!.GetValue<string>().Should().Be("RepositoryConcurrency");
    }

    private async Task RegisterAreaWithMemberAsync(Guid areaId, Guid userId)
    {
        var createAreaResponse = await _client.PostAsJsonAsync("/api/v1/cleaning-areas", new
        {
            areaId,
            name = "Operations",
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
                    spotName = "Kitchen",
                    sortOrder = 10
                }
            }
        }, TestContext.Current.CancellationToken);
        createAreaResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
        var areaGet = await _client.GetAsync($"/api/v1/cleaning-areas/{areaId}", TestContext.Current.CancellationToken);
        var areaGetBody = await areaGet.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        areaGet.StatusCode.Should().Be(HttpStatusCode.OK, areaGetBody);
        var etag = areaGet.Headers.ETag!.Tag;

        using var assignRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cleaning-areas/{areaId}/members");
        assignRequest.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
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

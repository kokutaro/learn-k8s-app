using System.Net.Http.Headers;

namespace OsoujiSystem.WebApi.Tests;

internal static class ApiTestHelper
{
    public const string CurrentWeek = "2026-W10";
    public const string NextWeek = "2026-W11";
    public const string FutureWeek = "2026-W12";
    public static readonly Guid LegacyFacilityId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static async Task RegisterAreaAsync(
        HttpClient client,
        Guid areaId,
        string name,
        params (Guid SpotId, string SpotName, int SortOrder)[] spots)
    {
        var response = await client.PostAsJsonAsync("/api/v1/cleaning-areas", new
        {
            facilityId = LegacyFacilityId,
            areaId,
            name,
            initialWeekRule = new
            {
                startDay = "monday",
                startTime = "09:00:00",
                timeZoneId = "Asia/Tokyo",
                effectiveFromWeek = CurrentWeek
            },
            initialSpots = spots.Select(spot => new
            {
                spotId = spot.SpotId,
                spotName = spot.SpotName,
                sortOrder = spot.SortOrder
            }).ToArray()
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    public static async Task<string> GetAreaEtagAsync(ApiIntegrationTestFixture fixture, HttpClient client, Guid areaId)
    {
        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
        var response = await client.GetAsync($"/api/v1/cleaning-areas/{areaId}", TestContext.Current.CancellationToken);
        var bodyText = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, bodyText);
        response.Headers.ETag.Should().NotBeNull();
        return response.Headers.ETag!.Tag;
    }

    public static async Task<JsonObject> GetAreaAsync(ApiIntegrationTestFixture fixture, HttpClient client, Guid areaId)
    {
        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
        var response = await client.GetAsync($"/api/v1/cleaning-areas/{areaId}", TestContext.Current.CancellationToken);
        var bodyText = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, bodyText);
        return JsonNode.Parse(bodyText)!.AsObject();
    }

    public static async Task<HttpResponseMessage> AssignUserAsync(
        HttpClient client,
        Guid areaId,
        Guid userId,
        string etag,
        string employeeNumber)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cleaning-areas/{areaId}/members");
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
        request.Content = JsonContent.Create(new
        {
            userId,
            employeeNumber
        });

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static async Task<HttpResponseMessage> GeneratePlanAsync(
        HttpClient client,
        Guid areaId,
        string weekId = CurrentWeek,
        int fairnessWindowWeeks = 4)
    {
        return await client.PostAsJsonAsync("/api/v1/weekly-duty-plans", new
        {
            areaId,
            weekId,
            policy = new
            {
                fairnessWindowWeeks
            }
        }, TestContext.Current.CancellationToken);
    }

    public static async Task<(Guid PlanId, JsonObject Body)> GeneratePlanAndGetBodyAsync(
        HttpClient client,
        Guid areaId,
        string weekId = CurrentWeek,
        int fairnessWindowWeeks = 4)
    {
        var response = await GeneratePlanAsync(client, areaId, weekId, fairnessWindowWeeks);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        body.Should().NotBeNull();

        return (Guid.Parse(body["data"]!["planId"]!.GetValue<string>()), body);
    }

    public static async Task<JsonObject> GetPlanAsync(ApiIntegrationTestFixture fixture, HttpClient client, Guid planId)
    {
        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
        var response = await client.GetAsync($"/api/v1/weekly-duty-plans/{planId}", TestContext.Current.CancellationToken);
        var bodyText = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, bodyText);
        return JsonNode.Parse(bodyText)!.AsObject();
    }

    public static async Task<string> GetPlanEtagAsync(ApiIntegrationTestFixture fixture, HttpClient client, Guid planId)
    {
        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
        var response = await client.GetAsync($"/api/v1/weekly-duty-plans/{planId}", TestContext.Current.CancellationToken);
        var bodyText = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, bodyText);
        response.Headers.ETag.Should().NotBeNull();
        return response.Headers.ETag!.Tag;
    }
}

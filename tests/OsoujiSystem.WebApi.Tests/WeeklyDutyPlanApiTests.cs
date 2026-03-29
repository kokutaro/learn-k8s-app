using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.UserManagement;
using OsoujiSystem.Domain.ValueObjects;

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
        await RegisterAreaWithMemberAsync(areaId, userId, "000001");
        await SeedUserDirectoryAsync(new UserId(userId), "000001", "Hanako", ManagedUserLifecycleStatus.Active);

        var (planId, createBody) = await ApiTestHelper.GeneratePlanAndGetBodyAsync(_client, areaId);

        createBody["data"]!["status"]!.GetValue<string>().Should().Be("draft");
        createBody["data"]!["revision"]!.GetValue<int>().Should().Be(1);
        createBody["data"]!["weekLabel"]!.GetValue<string>().Should().Be("2026/3/2 週");

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
        var getResponse = await _client.GetAsync($"/api/v1/weekly-duty-plans/{planId}", TestContext.Current.CancellationToken);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.Headers.ETag!.Tag.Should().Be("\"2\"");

        var detail = await getResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        detail!["data"]!["areaId"]!.GetValue<string>().Should().Be(areaId.ToString());
        detail["data"]!["weekLabel"]!.GetValue<string>().Should().Be("2026/3/2 週");
        detail["data"]!["assignments"]!.AsArray().Should().HaveCount(1);
        detail["data"]!["assignments"]![0]!["userId"]!.GetValue<string>().Should().Be(userId.ToString());
        detail["data"]!["assignments"]![0]!["user"]!["displayName"]!.GetValue<string>().Should().Be("Hanako");
        detail["data"]!["assignments"]![0]!["user"]!["employeeNumber"]!.GetValue<string>().Should().Be("000001");

        using var publishRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/weekly-duty-plans/{planId}/publication");
        publishRequest.Headers.IfMatch.Add(new EntityTagHeaderValue("\"2\""));
        publishRequest.Content = JsonContent.Create(new { });

        var publishResponse = await _client.SendAsync(publishRequest, TestContext.Current.CancellationToken);
        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        publishResponse.Headers.ETag!.Tag.Should().Be("\"3\"");

        var publishBody = await publishResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        publishBody!["data"]!["planId"]!.GetValue<string>().Should().Be(planId.ToString());
        publishBody["data"]!["status"]!.GetValue<string>().Should().Be("published");

        var publishedDetail = await ApiTestHelper.GetPlanAsync(fixture, _client, planId);
        publishedDetail["data"]!["status"]!.GetValue<string>().Should().Be("published");
    }

    [Fact]
    public async Task GenerateWeeklyPlan_ForSameAreaAndWeekTwice_ShouldReturnConflict()
    {
        var areaId = Guid.NewGuid();
        await RegisterAreaWithMemberAsync(areaId, Guid.NewGuid(), "000001");

        var request = new
        {
            areaId,
            weekId = ApiTestHelper.CurrentWeek,
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
        await RegisterAreaWithMemberAsync(areaId, Guid.NewGuid(), "000001");

        var (planId, _) = await ApiTestHelper.GeneratePlanAndGetBodyAsync(_client, areaId);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/weekly-duty-plans/{planId}/publication");
        request.Headers.TryAddWithoutValidation("If-Match", "\"99\"");
        request.Content = JsonContent.Create(new { });

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["error"]!["code"]!.GetValue<string>().Should().Be("RepositoryConcurrency");
    }

    [Fact]
    public async Task CloseWeeklyPlan_ShouldBeIdempotentAndRejectRepublish()
    {
        var areaId = Guid.NewGuid();
        await RegisterAreaWithMemberAsync(areaId, Guid.NewGuid(), "000001");
        var (planId, _) = await ApiTestHelper.GeneratePlanAndGetBodyAsync(_client, areaId);

        var planEtag = await ApiTestHelper.GetPlanEtagAsync(fixture, _client, planId);
        using var firstClose = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/weekly-duty-plans/{planId}/closure");
        firstClose.Headers.TryAddWithoutValidation("If-Match", planEtag);
        firstClose.Content = JsonContent.Create(new { });

        var firstCloseResponse = await _client.SendAsync(firstClose, TestContext.Current.CancellationToken);
        firstCloseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        firstCloseResponse.Headers.ETag!.Tag.Should().Be("\"3\"");

        using var secondClose = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/weekly-duty-plans/{planId}/closure");
        secondClose.Headers.TryAddWithoutValidation("If-Match", "\"3\"");
        secondClose.Content = JsonContent.Create(new { });

        var secondCloseResponse = await _client.SendAsync(secondClose, TestContext.Current.CancellationToken);
        secondCloseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondCloseResponse.Headers.ETag!.Tag.Should().Be("\"3\"");

        using var publishRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/weekly-duty-plans/{planId}/publication");
        publishRequest.Headers.TryAddWithoutValidation("If-Match", "\"3\"");
        publishRequest.Content = JsonContent.Create(new { });

        var publishResponse = await _client.SendAsync(publishRequest, TestContext.Current.CancellationToken);

        publishResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var publishBody = await publishResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        publishBody!["error"]!["code"]!.GetValue<string>().Should().Be("WeekAlreadyClosedError");

        var planDetail = await ApiTestHelper.GetPlanAsync(fixture, _client, planId);
        planDetail["data"]!["status"]!.GetValue<string>().Should().Be("closed");
        planDetail["data"]!["revision"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task ListWeeklyDutyPlans_ShouldSupportFiltersSortingAndCursor()
    {
        var areaAId = Guid.NewGuid();
        var areaBId = Guid.NewGuid();

        await RegisterAreaWithMemberAsync(areaAId, Guid.NewGuid(), "000001");
        await RegisterAreaWithMemberAsync(areaBId, Guid.NewGuid(), "000002");

        var (areaAPlanId, _) = await ApiTestHelper.GeneratePlanAndGetBodyAsync(_client, areaAId);
        await ApiTestHelper.GeneratePlanAndGetBodyAsync(_client, areaBId, ApiTestHelper.NextWeek);

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);

        var filtered = await _client.GetFromJsonAsync<JsonObject>(
            $"/api/v1/weekly-duty-plans?areaId={areaAId}&weekId={ApiTestHelper.CurrentWeek}&status=draft&sort=weekId&limit=1",
            TestContext.Current.CancellationToken);

        filtered!["data"]!.AsArray().Should().HaveCount(1);
        filtered["data"]![0]!["id"]!.GetValue<string>().Should().Be(areaAPlanId.ToString());
        filtered["data"]![0]!["weekLabel"]!.GetValue<string>().Should().Be("2026/3/2 週");
        filtered["meta"]!["hasNext"]!.GetValue<bool>().Should().BeFalse();

        var paged = await _client.GetFromJsonAsync<JsonObject>(
            "/api/v1/weekly-duty-plans?sort=createdAt&limit=1",
            TestContext.Current.CancellationToken);

        paged!["data"]!.AsArray().Should().HaveCount(1);
        paged["meta"]!["hasNext"]!.GetValue<bool>().Should().BeTrue();
        var cursor = paged["meta"]!["nextCursor"]!.GetValue<string>();
        cursor.Should().NotBeNullOrWhiteSpace();

        var secondPage = await _client.GetFromJsonAsync<JsonObject>(
            $"/api/v1/weekly-duty-plans?sort=createdAt&limit=1&cursor={Uri.EscapeDataString(cursor)}",
            TestContext.Current.CancellationToken);

        secondPage!["data"]!.AsArray().Should().HaveCount(1);
    }

    private async Task RegisterAreaWithMemberAsync(Guid areaId, Guid userId, string employeeNumber)
    {
        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Operations", (Guid.NewGuid(), "Kitchen", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);

        var assignResponse = await ApiTestHelper.AssignUserAsync(_client, areaId, userId, etag, employeeNumber);
        assignResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedUserDirectoryAsync(UserId userId, string employeeNumber, string displayName, ManagedUserLifecycleStatus lifecycleStatus)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserDirectoryProjectionRepository>();
        await repository.UpsertAsync(
            new UserDirectoryProjection(
                userId,
                EmployeeNumber.Create(employeeNumber).Value,
                displayName,
                lifecycleStatus,
                "OPS",
                1),
            1,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);
    }
}

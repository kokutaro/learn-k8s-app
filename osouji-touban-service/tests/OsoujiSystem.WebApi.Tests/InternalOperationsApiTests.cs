namespace OsoujiSystem.WebApi.Tests;

[Collection(ApiIntegrationTestCollection.Name)]
public sealed class InternalOperationsApiTests(ApiIntegrationTestFixture fixture) : IAsyncLifetime
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
    public async Task ApplyDueWeekRuleChanges_ShouldApplyOnlyWhenEffectiveWeekIsReached()
    {
        var areaId = Guid.NewGuid();
        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "HQ", (Guid.NewGuid(), "Lobby", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);

        using var scheduleRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/cleaning-areas/{areaId}/pending-week-rule");
        scheduleRequest.Headers.TryAddWithoutValidation("If-Match", etag);
        scheduleRequest.Content = JsonContent.Create(new
        {
            startDay = "tuesday",
            startTime = "08:30:00",
            timeZoneId = "Asia/Tokyo",
            effectiveFromWeek = ApiTestHelper.FutureWeek
        });

        var scheduleResponse = await _client.SendAsync(scheduleRequest, TestContext.Current.CancellationToken);
        scheduleResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var earlyApplyResponse = await _client.PostAsJsonAsync("/api/v1/internal/week-rule-applications", new
        {
            currentWeek = ApiTestHelper.NextWeek
        }, TestContext.Current.CancellationToken);

        earlyApplyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var earlyBody = await earlyApplyResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        earlyBody!["data"]!["appliedCount"]!.GetValue<int>().Should().Be(0);

        var beforeApplyArea = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        beforeApplyArea["data"]!["currentWeekRule"]!["startDay"]!.GetValue<string>().Should().Be("monday");
        beforeApplyArea["data"]!["pendingWeekRule"]!["startDay"]!.GetValue<string>().Should().Be("tuesday");

        var dueApplyResponse = await _client.PostAsJsonAsync("/api/v1/internal/week-rule-applications", new
        {
            currentWeek = ApiTestHelper.FutureWeek
        }, TestContext.Current.CancellationToken);

        dueApplyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var dueBody = await dueApplyResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        dueBody!["data"]!["appliedCount"]!.GetValue<int>().Should().Be(1);

        var appliedArea = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        appliedArea["data"]!["currentWeekRule"]!["startDay"]!.GetValue<string>().Should().Be("tuesday");
        appliedArea["data"]!["currentWeekRule"]!["effectiveFromWeek"]!.GetValue<string>().Should().Be(ApiTestHelper.FutureWeek);
        appliedArea["data"]!["pendingWeekRule"].Should().BeNull();
    }

    [Fact]
    public async Task ApplyDueWeekRuleChanges_WithInvalidWeek_ShouldReturnBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/internal/week-rule-applications", new
        {
            currentWeek = "2026-W99"
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["errors"]!["currentWeek"]!.AsArray().Should().ContainSingle();
    }

    [Fact]
    public async Task GenerateCurrentWeekPlansBatch_ShouldReturnGeneratedSkippedAndFailedCounts()
    {
        var generatedAreaId = Guid.NewGuid();
        var skippedAreaId = Guid.NewGuid();
        var failedAreaId = Guid.NewGuid();

        await RegisterAreaWithOptionalMemberAsync(generatedAreaId, true, "000001");
        await RegisterAreaWithOptionalMemberAsync(skippedAreaId, true, "000002");
        await RegisterAreaWithOptionalMemberAsync(failedAreaId, false, null);

        var preexistingResponse = await ApiTestHelper.GeneratePlanAsync(_client, skippedAreaId);
        preexistingResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var batchResponse = await _client.PostAsJsonAsync("/api/v1/internal/current-week-plan-generations", new
        {
            policy = new
            {
                fairnessWindowWeeks = 4
            }
        }, TestContext.Current.CancellationToken);

        batchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await batchResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["data"]!["generatedCount"]!.GetValue<int>().Should().Be(1);
        body["data"]!["skippedCount"]!.GetValue<int>().Should().Be(1);
        body["data"]!["failedCount"]!.GetValue<int>().Should().Be(1);

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);

        var generatedPlans = await _client.GetFromJsonAsync<JsonObject>(
            $"/api/v1/weekly-duty-plans?areaId={generatedAreaId}&weekId={ApiTestHelper.CurrentWeek}",
            TestContext.Current.CancellationToken);
        generatedPlans!["data"]!.AsArray().Should().ContainSingle();

        var skippedPlans = await _client.GetFromJsonAsync<JsonObject>(
            $"/api/v1/weekly-duty-plans?areaId={skippedAreaId}&weekId={ApiTestHelper.CurrentWeek}",
            TestContext.Current.CancellationToken);
        skippedPlans!["data"]!.AsArray().Should().ContainSingle();

        var failedPlans = await _client.GetFromJsonAsync<JsonObject>(
            $"/api/v1/weekly-duty-plans?areaId={failedAreaId}&weekId={ApiTestHelper.CurrentWeek}",
            TestContext.Current.CancellationToken);
        failedPlans!["data"]!.AsArray().Should().BeEmpty();
    }

    private async Task RegisterAreaWithOptionalMemberAsync(Guid areaId, bool withMember, string? employeeNumber)
    {
        await ApiTestHelper.RegisterAreaAsync(_client, areaId, $"Area-{areaId:N}", (Guid.NewGuid(), "Spot", 10));

        if (!withMember)
        {
            await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
            return;
        }

        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        var response = await ApiTestHelper.AssignUserAsync(_client, areaId, Guid.NewGuid(), etag, employeeNumber!);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
    }
}

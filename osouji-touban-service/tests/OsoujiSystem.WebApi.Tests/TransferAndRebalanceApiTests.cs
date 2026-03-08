namespace OsoujiSystem.WebApi.Tests;

[Collection(ApiIntegrationTestCollection.Name)]
public sealed class TransferAndRebalanceApiTests(ApiIntegrationTestFixture fixture) : IAsyncLifetime
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
    public async Task AssignUser_WithCurrentWeekPlan_ShouldRebalanceAndIncreaseRevision()
    {
        var areaId = Guid.NewGuid();
        var existingUserId = Guid.NewGuid();
        await RegisterAreaWithMembersAsync(areaId, "Ops", new[] { (existingUserId, "000001") }, (Guid.NewGuid(), "Sink", 10));
        var (planId, _) = await ApiTestHelper.GeneratePlanAndGetBodyAsync(_client, areaId);

        var initialPlan = await ApiTestHelper.GetPlanAsync(fixture, _client, planId);
        initialPlan["data"]!["revision"]!.GetValue<int>().Should().Be(1);

        var areaEtag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        var addedUserId = Guid.NewGuid();
        var assignResponse = await ApiTestHelper.AssignUserAsync(_client, areaId, addedUserId, areaEtag, "000002");

        assignResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var rebalancedPlan = await ApiTestHelper.GetPlanAsync(fixture, _client, planId);
        rebalancedPlan["data"]!["revision"]!.GetValue<int>().Should().Be(2);
        rebalancedPlan["data"]!["assignments"]!.AsArray().Should().HaveCount(1);
        rebalancedPlan["data"]!["offDutyEntries"]!.AsArray().Should().ContainSingle();
        rebalancedPlan["data"]!["offDutyEntries"]![0]!["userId"]!.GetValue<string>().Should().Be(addedUserId.ToString());
    }

    [Fact]
    public async Task UnassignUser_WithCurrentWeekPlan_ShouldRemoveUserAndIncreaseRevision()
    {
        var areaId = Guid.NewGuid();
        var removedUserId = Guid.NewGuid();
        var remainingUserId = Guid.NewGuid();
        await RegisterAreaWithMembersAsync(
            areaId,
            "Ops",
            new[] { (removedUserId, "000001"), (remainingUserId, "000002") },
            (Guid.NewGuid(), "Pantry", 10),
            (Guid.NewGuid(), "Lobby", 20));

        var (planId, _) = await ApiTestHelper.GeneratePlanAndGetBodyAsync(_client, areaId);
        var areaEtag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cleaning-areas/{areaId}/members/{removedUserId}");
        request.Headers.TryAddWithoutValidation("If-Match", areaEtag);

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var plan = await ApiTestHelper.GetPlanAsync(fixture, _client, planId);
        plan["data"]!["revision"]!.GetValue<int>().Should().Be(2);
        plan["data"]!["assignments"]!.AsArray().Should().OnlyContain(node => node!["userId"]!.GetValue<string>() == remainingUserId.ToString());
        plan["data"]!["offDutyEntries"]!.AsArray().Should().NotContain(node => node!["userId"]!.GetValue<string>() == removedUserId.ToString());

        var area = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        area["data"]!["members"]!.AsArray().Should().ContainSingle();
        area["data"]!["members"]![0]!["userId"]!.GetValue<string>().Should().Be(remainingUserId.ToString());
    }

    [Fact]
    public async Task AssignUser_WithoutCurrentWeekPlan_ShouldOnlyUpdateAreaMembership()
    {
        var areaId = Guid.NewGuid();
        await RegisterAreaWithMembersAsync(areaId, "Ops", Array.Empty<(Guid UserId, string EmployeeNumber)>(), (Guid.NewGuid(), "Sink", 10));

        var areaEtag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        var addedUserId = Guid.NewGuid();
        var response = await ApiTestHelper.AssignUserAsync(_client, areaId, addedUserId, areaEtag, "000001");

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
        var plans = await _client.GetFromJsonAsync<JsonObject>(
            $"/api/v1/weekly-duty-plans?areaId={areaId}&weekId={ApiTestHelper.CurrentWeek}",
            TestContext.Current.CancellationToken);

        plans!["data"]!.AsArray().Should().BeEmpty();
        var area = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        area["data"]!["members"]!.AsArray().Should().ContainSingle();
        area["data"]!["members"]![0]!["userId"]!.GetValue<string>().Should().Be(addedUserId.ToString());
    }

    [Fact]
    public async Task AddAndRemoveSpot_WithCurrentWeekPlan_ShouldRecalculatePlan()
    {
        var areaId = Guid.NewGuid();
        await RegisterAreaWithMembersAsync(areaId, "Ops", new[] { (Guid.NewGuid(), "000001") }, (Guid.NewGuid(), "Sink", 10));
        var (planId, _) = await ApiTestHelper.GeneratePlanAndGetBodyAsync(_client, areaId);

        var addedSpotId = Guid.NewGuid();
        var firstAreaEtag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        using var addRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cleaning-areas/{areaId}/spots");
        addRequest.Headers.TryAddWithoutValidation("If-Match", firstAreaEtag);
        addRequest.Content = JsonContent.Create(new
        {
            spotId = addedSpotId,
            name = "Lobby",
            sortOrder = 20
        });

        var addResponse = await _client.SendAsync(addRequest, TestContext.Current.CancellationToken);
        addResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var afterAddPlan = await ApiTestHelper.GetPlanAsync(fixture, _client, planId);
        afterAddPlan["data"]!["revision"]!.GetValue<int>().Should().Be(2);
        afterAddPlan["data"]!["assignments"]!.AsArray().Should().HaveCount(2);

        var secondAreaEtag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        using var removeRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cleaning-areas/{areaId}/spots/{addedSpotId}");
        removeRequest.Headers.TryAddWithoutValidation("If-Match", secondAreaEtag);

        var removeResponse = await _client.SendAsync(removeRequest, TestContext.Current.CancellationToken);
        removeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterRemovePlan = await ApiTestHelper.GetPlanAsync(fixture, _client, planId);
        afterRemovePlan["data"]!["revision"]!.GetValue<int>().Should().Be(3);
        afterRemovePlan["data"]!["assignments"]!.AsArray().Should().HaveCount(1);
    }

    [Fact]
    public async Task TransferUserToArea_ShouldUpdateAreasAndRebalanceBothPlans()
    {
        var sourceAreaId = Guid.NewGuid();
        var targetAreaId = Guid.NewGuid();
        var transferredUserId = Guid.NewGuid();

        await RegisterAreaWithMembersAsync(
            sourceAreaId,
            "Source",
            new[] { (transferredUserId, "000001"), (Guid.NewGuid(), "000002") },
            (Guid.NewGuid(), "Source Spot", 10));

        await RegisterAreaWithMembersAsync(
            targetAreaId,
            "Target",
            new[] { (Guid.NewGuid(), "000003") },
            (Guid.NewGuid(), "Target Spot", 10));

        var (sourcePlanId, _) = await ApiTestHelper.GeneratePlanAndGetBodyAsync(_client, sourceAreaId);
        var (targetPlanId, _) = await ApiTestHelper.GeneratePlanAndGetBodyAsync(_client, targetAreaId);

        var sourceArea = await ApiTestHelper.GetAreaAsync(fixture, _client, sourceAreaId);
        var targetArea = await ApiTestHelper.GetAreaAsync(fixture, _client, targetAreaId);

        var response = await _client.PostAsJsonAsync("/api/v1/area-member-transfers", new
        {
            fromAreaId = sourceAreaId,
            toAreaId = targetAreaId,
            userId = transferredUserId,
            toAreaMemberId = Guid.NewGuid(),
            employeeNumber = "000001",
            fromAreaVersion = sourceArea["data"]!["version"]!.GetValue<long>(),
            toAreaVersion = targetArea["data"]!["version"]!.GetValue<long>()
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["data"]!["transferred"]!.GetValue<bool>().Should().BeTrue();

        var refreshedSourceArea = await ApiTestHelper.GetAreaAsync(fixture, _client, sourceAreaId);
        var refreshedTargetArea = await ApiTestHelper.GetAreaAsync(fixture, _client, targetAreaId);
        refreshedSourceArea["data"]!["members"]!.AsArray().Should().NotContain(node => node!["userId"]!.GetValue<string>() == transferredUserId.ToString());
        refreshedTargetArea["data"]!["members"]!.AsArray().Should().Contain(node => node!["userId"]!.GetValue<string>() == transferredUserId.ToString());

        var refreshedSourcePlan = await ApiTestHelper.GetPlanAsync(fixture, _client, sourcePlanId);
        var refreshedTargetPlan = await ApiTestHelper.GetPlanAsync(fixture, _client, targetPlanId);

        refreshedSourcePlan["data"]!["revision"]!.GetValue<int>().Should().BeGreaterThan(1);
        refreshedTargetPlan["data"]!["revision"]!.GetValue<int>().Should().BeGreaterThan(1);
        refreshedSourcePlan["data"]!["assignments"]!.AsArray().Should().NotContain(node => node!["userId"]!.GetValue<string>() == transferredUserId.ToString());
        refreshedSourcePlan["data"]!["offDutyEntries"]!.AsArray().Should().NotContain(node => node!["userId"]!.GetValue<string>() == transferredUserId.ToString());
        var targetAssignments = refreshedTargetPlan["data"]!["assignments"]!.AsArray();
        var targetOffDutyEntries = refreshedTargetPlan["data"]!["offDutyEntries"]!.AsArray();
        var transferredPresentInTargetPlan = targetAssignments.Any(node => node!["userId"]!.GetValue<string>() == transferredUserId.ToString())
            || targetOffDutyEntries.Any(node => node!["userId"]!.GetValue<string>() == transferredUserId.ToString());
        transferredPresentInTargetPlan.Should().BeTrue();
    }

    [Fact]
    public async Task TransferUserToArea_WithSameSourceAndTarget_ShouldReturnConflict()
    {
        var areaId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await RegisterAreaWithMembersAsync(areaId, "Same", new[] { (userId, "000001") }, (Guid.NewGuid(), "Spot", 10));
        var area = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);

        var response = await _client.PostAsJsonAsync("/api/v1/area-member-transfers", new
        {
            fromAreaId = areaId,
            toAreaId = areaId,
            userId,
            toAreaMemberId = Guid.NewGuid(),
            employeeNumber = "000001",
            fromAreaVersion = area["data"]!["version"]!.GetValue<long>(),
            toAreaVersion = area["data"]!["version"]!.GetValue<long>()
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        body!["error"]!["code"]!.GetValue<string>().Should().Be("InvalidTransferRequest");
    }

    [Fact]
    public async Task AddSpot_WithClosedPlan_ShouldSucceedWithoutChangingPlan()
    {
        var areaId = Guid.NewGuid();
        await RegisterAreaWithMembersAsync(areaId, "Closed", new[] { (Guid.NewGuid(), "000001") }, (Guid.NewGuid(), "Sink", 10));
        var (planId, _) = await ApiTestHelper.GeneratePlanAndGetBodyAsync(_client, areaId);

        var planEtag = await ApiTestHelper.GetPlanEtagAsync(fixture, _client, planId);
        using var closeRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/weekly-duty-plans/{planId}/closure");
        closeRequest.Headers.TryAddWithoutValidation("If-Match", planEtag);
        closeRequest.Content = JsonContent.Create(new { });
        var closeResponse = await _client.SendAsync(closeRequest, TestContext.Current.CancellationToken);
        closeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var areaEtag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        using var addSpotRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cleaning-areas/{areaId}/spots");
        addSpotRequest.Headers.TryAddWithoutValidation("If-Match", areaEtag);
        addSpotRequest.Content = JsonContent.Create(new
        {
            spotId = Guid.NewGuid(),
            name = "Lobby",
            sortOrder = 20
        });

        var addSpotResponse = await _client.SendAsync(addSpotRequest, TestContext.Current.CancellationToken);
        addSpotResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var plan = await ApiTestHelper.GetPlanAsync(fixture, _client, planId);
        plan["data"]!["status"]!.GetValue<string>().Should().Be("closed");
        plan["data"]!["revision"]!.GetValue<int>().Should().Be(1);
        plan["data"]!["assignments"]!.AsArray().Should().HaveCount(1);

        var area = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        area["data"]!["spots"]!.AsArray().Should().HaveCount(2);
    }

    private async Task RegisterAreaWithMembersAsync(
        Guid areaId,
        string name,
        IReadOnlyList<(Guid UserId, string EmployeeNumber)> members,
        params (Guid SpotId, string SpotName, int SortOrder)[] spots)
    {
        await ApiTestHelper.RegisterAreaAsync(_client, areaId, name, spots);

        foreach (var (userId, employeeNumber) in members)
        {
            var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
            var response = await ApiTestHelper.AssignUserAsync(_client, areaId, userId, etag, employeeNumber);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);
    }
}

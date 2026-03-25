using Microsoft.Extensions.DependencyInjection;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.UserManagement;
using OsoujiSystem.Domain.ValueObjects;

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
        getBody["data"]!["currentWeekRule"]!["effectiveFromWeekLabel"]!.GetValue<string>().Should().Be("2026/3/2 週");
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
        body["data"]!["pendingWeekRule"]!["effectiveFromWeekLabel"]!.GetValue<string>().Should().Be("2026/3/17 週");

        var detail = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        detail["data"]!["pendingWeekRule"]!["effectiveFromWeek"]!.GetValue<string>().Should().Be(ApiTestHelper.FutureWeek);
        detail["data"]!["pendingWeekRule"]!["effectiveFromWeekLabel"]!.GetValue<string>().Should().Be("2026/3/17 週");
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
        body["data"]!["weekLabel"]!.GetValue<string>().Should().Be("2026/3/2 週");
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

    [Fact]
    public async Task AssignUserToArea_WithUserInDirectory_ShouldReturnDisplayNameInMemberList()
    {
        var areaId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await SeedUserDirectoryAsync(new UserId(userId), "000001", "田中 花子", ManagedUserLifecycleStatus.Active);

        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        (await ApiTestHelper.AssignUserAsync(_client, areaId, userId, etag, "000001")).StatusCode.Should().Be(HttpStatusCode.Created);

        var refreshed = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        refreshed["data"]!["members"]!.AsArray().Should().HaveCount(1);
        refreshed["data"]!["members"]![0]!["displayName"]!.GetValue<string>().Should().Be("田中 花子");
    }

    [Fact]
    public async Task AssignUserToArea_WithNoUserInDirectory_ShouldReturnNullDisplayName()
    {
        var areaId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        (await ApiTestHelper.AssignUserAsync(_client, areaId, userId, etag, "000001")).StatusCode.Should().Be(HttpStatusCode.Created);

        var refreshed = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        refreshed["data"]!["members"]!.AsArray().Should().HaveCount(1);
        refreshed["data"]!["members"]![0]!["displayName"].Should().BeNull();
    }

    [Fact]
    public async Task AssignUserToArea_WithEmptyDisplayNameInDirectory_ShouldReturnNullDisplayName()
    {
        var areaId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await SeedUserDirectoryAsync(new UserId(userId), "000001", string.Empty, ManagedUserLifecycleStatus.Active);

        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        (await ApiTestHelper.AssignUserAsync(_client, areaId, userId, etag, "000001")).StatusCode.Should().Be(HttpStatusCode.Created);

        var refreshed = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        refreshed["data"]!["members"]!.AsArray().Should().HaveCount(1);
        refreshed["data"]!["members"]![0]!["displayName"].Should().BeNull();
    }

    [Fact]
    public async Task BackfillMemberDisplayNameMigration_ShouldImproveMembersDisplayNameAfterExecution()
    {
        var areaId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();
        var sourceUserId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();

        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        (await ApiTestHelper.AssignUserAsync(_client, areaId, memberUserId, etag, "000001")).StatusCode.Should().Be(HttpStatusCode.Created);

        await fixture.ExecuteSqlAsync(
            $"""
            INSERT INTO projection_user_directory (
                user_id,
                employee_number,
                display_name,
                lifecycle_status,
                department_code,
                source_event_id,
                aggregate_version,
                email_address,
                updated_at
            )
            VALUES (
                '{memberUserId}'::uuid,
                '000001',
                '',
                'Active',
                'OPS',
                '{Guid.NewGuid()}'::uuid,
                1,
                'member@example.com',
                now()
            )
            ON CONFLICT (user_id)
            DO UPDATE SET
                display_name = EXCLUDED.display_name,
                updated_at = now();
            """,
            TestContext.Current.CancellationToken);

        await fixture.ExecuteSqlAsync(
            $"""
            INSERT INTO projection_user_directory (
                user_id,
                employee_number,
                display_name,
                lifecycle_status,
                department_code,
                source_event_id,
                aggregate_version,
                email_address,
                updated_at
            )
            VALUES (
                '{sourceUserId}'::uuid,
                '000001',
                '  田中 花子  ',
                'Active',
                'OPS',
                '{sourceEventId}'::uuid,
                10,
                'hanako@example.com',
                now()
            )
            ON CONFLICT (user_id)
            DO UPDATE SET
                employee_number = EXCLUDED.employee_number,
                display_name = EXCLUDED.display_name,
                lifecycle_status = EXCLUDED.lifecycle_status,
                department_code = EXCLUDED.department_code,
                source_event_id = EXCLUDED.source_event_id,
                aggregate_version = EXCLUDED.aggregate_version,
                email_address = EXCLUDED.email_address,
                updated_at = now();
            """,
            TestContext.Current.CancellationToken);

        var before = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        before["data"]!["members"]![0]!["displayName"].Should().BeNull();

        await fixture.ExecuteMigrationScriptAsync("0010_backfill_area_member_display_name.sql", TestContext.Current.CancellationToken);
        await fixture.FlushRedisAsync();

        var after = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        after["data"]!["members"]![0]!["displayName"]!.GetValue<string>().Should().Be("田中 花子");

        var persistedDisplayName = await fixture.ExecuteScalarAsync<string>(
            $"SELECT display_name FROM projection_user_directory WHERE user_id = '{memberUserId}'::uuid;",
            TestContext.Current.CancellationToken);
        persistedDisplayName.Should().Be("田中 花子");
    }

    [Fact]
    public async Task BackfillMemberDisplayNameMigration_WhenReExecuted_ShouldRemainIdempotent()
    {
        var areaId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();
        var sourceUserId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();

        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        (await ApiTestHelper.AssignUserAsync(_client, areaId, memberUserId, etag, "000001")).StatusCode.Should().Be(HttpStatusCode.Created);

        await fixture.ExecuteSqlAsync(
            $"""
            INSERT INTO projection_user_directory (
                user_id,
                employee_number,
                display_name,
                lifecycle_status,
                department_code,
                source_event_id,
                aggregate_version,
                email_address,
                updated_at
            )
            VALUES (
                '{memberUserId}'::uuid,
                '000001',
                '',
                'Active',
                'OPS',
                '{Guid.NewGuid()}'::uuid,
                1,
                'member2@example.com',
                now()
            )
            ON CONFLICT (user_id)
            DO UPDATE SET
                display_name = EXCLUDED.display_name,
                updated_at = now();
            """,
            TestContext.Current.CancellationToken);

        await fixture.ExecuteSqlAsync(
            $"""
            INSERT INTO projection_user_directory (
                user_id,
                employee_number,
                display_name,
                lifecycle_status,
                department_code,
                source_event_id,
                aggregate_version,
                email_address,
                updated_at
            )
            VALUES (
                '{sourceUserId}'::uuid,
                '000001',
                '鈴木 一郎',
                'Active',
                'OPS',
                '{sourceEventId}'::uuid,
                11,
                'ichiro@example.com',
                now()
            )
            ON CONFLICT (user_id)
            DO UPDATE SET
                employee_number = EXCLUDED.employee_number,
                display_name = EXCLUDED.display_name,
                lifecycle_status = EXCLUDED.lifecycle_status,
                department_code = EXCLUDED.department_code,
                source_event_id = EXCLUDED.source_event_id,
                aggregate_version = EXCLUDED.aggregate_version,
                email_address = EXCLUDED.email_address,
                updated_at = now();
            """,
            TestContext.Current.CancellationToken);

        await fixture.ExecuteMigrationScriptAsync("0010_backfill_area_member_display_name.sql", TestContext.Current.CancellationToken);
        await fixture.FlushRedisAsync();

        var updatedAtAfterFirstRun = await fixture.ExecuteScalarAsync<DateTime>(
            $"SELECT updated_at FROM projection_user_directory WHERE user_id = '{memberUserId}'::uuid;",
            TestContext.Current.CancellationToken);
        var rowCountAfterFirstRun = await fixture.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM projection_user_directory WHERE user_id = '{memberUserId}'::uuid;",
            TestContext.Current.CancellationToken);

        await fixture.ExecuteMigrationScriptAsync("0010_backfill_area_member_display_name.sql", TestContext.Current.CancellationToken);
        await fixture.FlushRedisAsync();

        var updatedAtAfterSecondRun = await fixture.ExecuteScalarAsync<DateTime>(
            $"SELECT updated_at FROM projection_user_directory WHERE user_id = '{memberUserId}'::uuid;",
            TestContext.Current.CancellationToken);
        var rowCountAfterSecondRun = await fixture.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM projection_user_directory WHERE user_id = '{memberUserId}'::uuid;",
            TestContext.Current.CancellationToken);

        rowCountAfterFirstRun.Should().Be(1);
        rowCountAfterSecondRun.Should().Be(1);
        updatedAtAfterSecondRun.Should().Be(updatedAtAfterFirstRun);
    }

    [Fact]
    public async Task BackfillMemberDisplayNameMigration_WithAmbiguousEmployeeNumber_ShouldLeaveDisplayNameNull()
    {
        var areaId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();

        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        (await ApiTestHelper.AssignUserAsync(_client, areaId, memberUserId, etag, "000003")).StatusCode.Should().Be(HttpStatusCode.Created);

        await fixture.ExecuteSqlAsync(
            $"""
            INSERT INTO projection_user_directory (
                user_id,
                employee_number,
                display_name,
                lifecycle_status,
                department_code,
                source_event_id,
                aggregate_version,
                email_address,
                updated_at
            )
            VALUES
                ('{Guid.NewGuid()}'::uuid, '000003', '候補A', 'Active', 'OPS', '{Guid.NewGuid()}'::uuid, 12, 'a@example.com', now()),
                ('{Guid.NewGuid()}'::uuid, '000003', '候補B', 'Active', 'OPS', '{Guid.NewGuid()}'::uuid, 13, 'b@example.com', now());
            """,
            TestContext.Current.CancellationToken);

        await fixture.ExecuteMigrationScriptAsync("0010_backfill_area_member_display_name.sql", TestContext.Current.CancellationToken);
        await fixture.FlushRedisAsync();

        var after = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        after["data"]!["members"]![0]!["displayName"].Should().BeNull();

        var memberDirectoryRows = await fixture.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM projection_user_directory WHERE user_id = '{memberUserId}'::uuid;",
            TestContext.Current.CancellationToken);
        memberDirectoryRows.Should().Be(0);
    }

    [Fact]
    public async Task BackfillMemberDisplayNameMigration_WhenMemberDirectoryRowMissing_ShouldInsertByEmployeeNumberUniqueMatch()
    {
        var areaId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();
        var sourceUserId = Guid.NewGuid();

        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        (await ApiTestHelper.AssignUserAsync(_client, areaId, memberUserId, etag, "000021")).StatusCode.Should().Be(HttpStatusCode.Created);
        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);

        await fixture.ExecuteSqlAsync(
            $"DELETE FROM projection_user_directory WHERE user_id = '{memberUserId}'::uuid;",
            TestContext.Current.CancellationToken);

        await fixture.ExecuteSqlAsync(
            $"""
            INSERT INTO projection_user_directory (
                user_id,
                employee_number,
                display_name,
                lifecycle_status,
                department_code,
                source_event_id,
                aggregate_version,
                email_address,
                updated_at
            )
            VALUES (
                '{sourceUserId}'::uuid,
                '000021',
                '中村 花',
                'Active',
                'OPS',
                '{Guid.NewGuid()}'::uuid,
                1,
                'source-21@example.com',
                now()
            );
            """,
            TestContext.Current.CancellationToken);

        var beforeRows = await fixture.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM projection_user_directory WHERE user_id = '{memberUserId}'::uuid;",
            TestContext.Current.CancellationToken);
        beforeRows.Should().Be(0);

        await fixture.ExecuteMigrationScriptAsync("0010_backfill_area_member_display_name.sql", TestContext.Current.CancellationToken);
        await fixture.FlushRedisAsync();

        var persistedDisplayName = await fixture.ExecuteScalarAsync<string>(
            $"SELECT display_name FROM projection_user_directory WHERE user_id = '{memberUserId}'::uuid;",
            TestContext.Current.CancellationToken);
        persistedDisplayName.Should().Be("中村 花");

        var area = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        area["data"]!["members"]![0]!["displayName"]!.GetValue<string>().Should().Be("中村 花");
    }

    [Fact]
    public async Task BackfillMemberDisplayNameMigration_WhenEmployeeNumberAlsoMatches_ShouldPreferUserId()
    {
        var areaId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();

        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        (await ApiTestHelper.AssignUserAsync(_client, areaId, memberUserId, etag, "000022")).StatusCode.Should().Be(HttpStatusCode.Created);
        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);

        await fixture.ExecuteSqlAsync(
            $"""
            INSERT INTO projection_user_directory (
                user_id,
                employee_number,
                display_name,
                lifecycle_status,
                department_code,
                source_event_id,
                aggregate_version,
                email_address,
                updated_at
            )
            VALUES
                ('{memberUserId}'::uuid, '000022', '本人 表示名', 'Active', 'OPS', '{Guid.NewGuid()}'::uuid, 1, 'self@example.com', now()),
                ('{Guid.NewGuid()}'::uuid, '000022', '他人 表示名', 'Active', 'OPS', '{Guid.NewGuid()}'::uuid, 2, 'other@example.com', now())
            ON CONFLICT (user_id)
            DO UPDATE SET
                employee_number = EXCLUDED.employee_number,
                display_name = EXCLUDED.display_name,
                lifecycle_status = EXCLUDED.lifecycle_status,
                department_code = EXCLUDED.department_code,
                source_event_id = EXCLUDED.source_event_id,
                aggregate_version = EXCLUDED.aggregate_version,
                email_address = EXCLUDED.email_address,
                updated_at = now();
            """,
            TestContext.Current.CancellationToken);

        await fixture.ExecuteMigrationScriptAsync("0010_backfill_area_member_display_name.sql", TestContext.Current.CancellationToken);
        await fixture.FlushRedisAsync();

        var persistedDisplayName = await fixture.ExecuteScalarAsync<string>(
            $"SELECT display_name FROM projection_user_directory WHERE user_id = '{memberUserId}'::uuid;",
            TestContext.Current.CancellationToken);
        persistedDisplayName.Should().Be("本人 表示名");

        var area = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        area["data"]!["members"]![0]!["displayName"]!.GetValue<string>().Should().Be("本人 表示名");
    }

    [Fact]
    public async Task BackfillMemberDisplayNameMigration_ShouldRecordExecutionMetrics()
    {
        var areaId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();

        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        (await ApiTestHelper.AssignUserAsync(_client, areaId, memberUserId, etag, "000023")).StatusCode.Should().Be(HttpStatusCode.Created);
        await fixture.DrainProjectionAsync(TestContext.Current.CancellationToken);

        await fixture.ExecuteSqlAsync(
            $"""
            INSERT INTO projection_user_directory (
                user_id,
                employee_number,
                display_name,
                lifecycle_status,
                department_code,
                source_event_id,
                aggregate_version,
                email_address,
                updated_at
            )
            VALUES
                ('{Guid.NewGuid()}'::uuid, '000023', '補完 候補', 'Active', 'OPS', '{Guid.NewGuid()}'::uuid, 1, 'source-23@example.com', now());
            """,
            TestContext.Current.CancellationToken);

        await fixture.ExecuteMigrationScriptAsync("0010_backfill_area_member_display_name.sql", TestContext.Current.CancellationToken);

        var targetMemberCount = await fixture.ExecuteScalarAsync<long>(
            "SELECT target_member_count FROM migration_area_member_display_name_backfill_runs ORDER BY run_id DESC LIMIT 1;",
            TestContext.Current.CancellationToken);
        var updatedMemberCount = await fixture.ExecuteScalarAsync<long>(
            "SELECT updated_member_count FROM migration_area_member_display_name_backfill_runs ORDER BY run_id DESC LIMIT 1;",
            TestContext.Current.CancellationToken);
        var unresolvedMemberCount = await fixture.ExecuteScalarAsync<long>(
            "SELECT unresolved_member_count FROM migration_area_member_display_name_backfill_runs ORDER BY run_id DESC LIMIT 1;",
            TestContext.Current.CancellationToken);
        var ambiguousMatchCount = await fixture.ExecuteScalarAsync<long>(
            "SELECT ambiguous_match_count FROM migration_area_member_display_name_backfill_runs ORDER BY run_id DESC LIMIT 1;",
            TestContext.Current.CancellationToken);
        var missingRateBefore = await fixture.ExecuteScalarAsync<decimal>(
            "SELECT missing_rate_before FROM migration_area_member_display_name_backfill_runs ORDER BY run_id DESC LIMIT 1;",
            TestContext.Current.CancellationToken);
        var missingRateAfter = await fixture.ExecuteScalarAsync<decimal>(
            "SELECT missing_rate_after FROM migration_area_member_display_name_backfill_runs ORDER BY run_id DESC LIMIT 1;",
            TestContext.Current.CancellationToken);

        targetMemberCount.Should().Be(1);
        updatedMemberCount.Should().Be(1);
        unresolvedMemberCount.Should().Be(0);
        ambiguousMatchCount.Should().Be(0);
        missingRateBefore.Should().Be(1m);
        missingRateAfter.Should().Be(0m);
    }

    [Fact]
    public async Task BackfillMemberDisplayNameMigration_WithExistingDisplayName_ShouldKeepCurrentValue()
    {
        var areaId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();

        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));
        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);
        (await ApiTestHelper.AssignUserAsync(_client, areaId, memberUserId, etag, "000004")).StatusCode.Should().Be(HttpStatusCode.Created);

        await fixture.ExecuteSqlAsync(
            $"""
            INSERT INTO projection_user_directory (
                user_id,
                employee_number,
                display_name,
                lifecycle_status,
                department_code,
                source_event_id,
                aggregate_version,
                email_address,
                updated_at
            )
            VALUES
                ('{memberUserId}'::uuid, '000004', '既存 名称', 'Active', 'OPS', '{Guid.NewGuid()}'::uuid, 10, 'existing@example.com', now()),
                ('{Guid.NewGuid()}'::uuid, '000004', '候補 名称', 'Active', 'OPS', '{Guid.NewGuid()}'::uuid, 11, 'candidate@example.com', now())
            ON CONFLICT (user_id)
            DO UPDATE SET
                employee_number = EXCLUDED.employee_number,
                display_name = EXCLUDED.display_name,
                lifecycle_status = EXCLUDED.lifecycle_status,
                department_code = EXCLUDED.department_code,
                source_event_id = EXCLUDED.source_event_id,
                aggregate_version = EXCLUDED.aggregate_version,
                email_address = EXCLUDED.email_address,
                updated_at = now();
            """,
            TestContext.Current.CancellationToken);

        await fixture.ExecuteMigrationScriptAsync("0010_backfill_area_member_display_name.sql", TestContext.Current.CancellationToken);
        await fixture.FlushRedisAsync();

        var persistedDisplayName = await fixture.ExecuteScalarAsync<string>(
            $"SELECT display_name FROM projection_user_directory WHERE user_id = '{memberUserId}'::uuid;",
            TestContext.Current.CancellationToken);
        persistedDisplayName.Should().Be("既存 名称");

        var area = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        area["data"]!["members"]![0]!["displayName"]!.GetValue<string>().Should().Be("既存 名称");
    }

    private async Task SeedUserDirectoryAsync(
        UserId userId,
        string employeeNumber,
        string displayName,
        ManagedUserLifecycleStatus lifecycleStatus)
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

using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.UserManagement;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.WebApi.Tests;

[Collection(ApiIntegrationTestCollection.Name)]
public sealed class UserManagementApiTests(ApiIntegrationTestFixture fixture) : IAsyncLifetime
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
    public async Task RegisterUser_ShouldCreateManagedUser()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users", new
        {
            employeeNumber = "123456",
            displayName = "Hanako",
            emailAddress = "hanako@example.com",
            departmentCode = "OPS",
            registrationSource = "adminPortal"
        }, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        body.Should().NotBeNull();
        body!["data"]!["employeeNumber"]!.GetValue<string>().Should().Be("123456");
        body["data"]!["lifecycleStatus"]!.GetValue<string>().Should().Be("Active");
        Guid.Parse(body["data"]!["userId"]!.GetValue<string>()).Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task UpdateUserProfile_WithIfMatch_ShouldAdvanceVersion()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/v1/users", new
        {
            employeeNumber = "123456",
            displayName = "Hanako",
            registrationSource = "adminPortal"
        }, TestContext.Current.CancellationToken);

        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        var userId = Guid.Parse(createBody!["data"]!["userId"]!.GetValue<string>());

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/users/{userId:D}");
        request.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
        request.Content = JsonContent.Create(new
        {
            displayName = "Hanako Updated",
            emailAddress = "hanako.updated@example.com",
            departmentCode = "HR"
        });

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.Should().Be("\"2\"");
        body!["data"]!["userId"]!.GetValue<string>().Should().Be(userId.ToString());
        body["data"]!["version"]!.GetValue<long>().Should().Be(2);
    }

    [Fact]
    public async Task ListUsers_ShouldSupportStatusQuerySortingAndCursor()
    {
        await SeedUserDirectoryAsync(new UserId(Guid.NewGuid()), "000002", "Mika", ManagedUserLifecycleStatus.Active);
        await SeedUserDirectoryAsync(new UserId(Guid.NewGuid()), "000001", "Aoi", ManagedUserLifecycleStatus.Active);
        await SeedUserDirectoryAsync(new UserId(Guid.NewGuid()), "000003", "Ren", ManagedUserLifecycleStatus.Suspended);

        var response = await _client.GetAsync(
            "/api/v1/users?status=active&query=OPS&sort=displayName&limit=1",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().NotBeNull();
        body!["data"]!.AsArray().Should().HaveCount(1);
        body["data"]![0]!["displayName"]!.GetValue<string>().Should().Be("Aoi");
        body["data"]![0]!["employeeNumber"]!.GetValue<string>().Should().Be("000001");
        body["data"]![0]!["lifecycleStatus"]!.GetValue<string>().Should().Be("active");
        body["meta"]!["hasNext"]!.GetValue<bool>().Should().BeTrue();

        var nextCursor = body["meta"]!["nextCursor"]!.GetValue<string>();
        var nextResponse = await _client.GetAsync(
            $"/api/v1/users?status=active&query=OPS&sort=displayName&limit=1&cursor={Uri.EscapeDataString(nextCursor)}",
            TestContext.Current.CancellationToken);
        var nextBody = await nextResponse.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);

        nextResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        nextBody.Should().NotBeNull();
        nextBody!["data"]!.AsArray().Should().HaveCount(1);
        nextBody["data"]![0]!["displayName"]!.GetValue<string>().Should().Be("Mika");
        nextBody["meta"]!["hasNext"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task ListUsers_ShouldSupportEmployeeNumberDescendingSort()
    {
        await SeedUserDirectoryAsync(new UserId(Guid.NewGuid()), "000001", "Aoi", ManagedUserLifecycleStatus.Active);
        await SeedUserDirectoryAsync(
            new UserId(Guid.NewGuid()),
            "000003",
            "Mika",
            ManagedUserLifecycleStatus.PendingActivation,
            departmentCode: null);
        await SeedUserDirectoryAsync(new UserId(Guid.NewGuid()), "000002", "Ren", ManagedUserLifecycleStatus.Suspended);

        var response = await _client.GetAsync(
            "/api/v1/users?sort=-employeeNumber",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().NotBeNull();
        body!["data"]!.AsArray().Should().HaveCount(3);
        body["data"]![0]!["employeeNumber"]!.GetValue<string>().Should().Be("000003");
        body["data"]![0]!["lifecycleStatus"]!.GetValue<string>().Should().Be("pendingActivation");
        body["data"]![0]!["departmentCode"].Should().BeNull();
        body["data"]![1]!["employeeNumber"]!.GetValue<string>().Should().Be("000002");
        body["data"]![2]!["employeeNumber"]!.GetValue<string>().Should().Be("000001");
    }

    [Fact]
    public async Task ListUsers_WithInvalidSort_ShouldReturnValidationError()
    {
        var response = await _client.GetAsync(
            "/api/v1/users?sort=createdAt",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().NotBeNull();
        body!["error"]!["code"]!.GetValue<string>().Should().Be("ValidationError");
        body["error"]!["details"]![0]!["field"]!.GetValue<string>().Should().Be("sort");
    }

    [Fact]
    public async Task AssignUserToArea_WithoutEmployeeNumber_ShouldUseUserDirectoryProjection()
    {
        var areaId = Guid.NewGuid();
        var userId = new UserId(Guid.NewGuid());
        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));
        await SeedUserDirectoryAsync(userId, "123456", "Hanako", ManagedUserLifecycleStatus.Active);

        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cleaning-areas/{areaId}/members");
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
        request.Content = JsonContent.Create(new
        {
            userId = userId.Value
        });

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        body!["data"]!["userId"]!.GetValue<string>().Should().Be(userId.ToString());

        var detail = await ApiTestHelper.GetAreaAsync(fixture, _client, areaId);
        detail["data"]!["members"]![0]!["employeeNumber"]!.GetValue<string>().Should().Be("123456");
    }

    [Fact]
    public async Task AssignUserToArea_WhenProjectionUserIsSuspended_ShouldReturnConflict()
    {
        var areaId = Guid.NewGuid();
        var userId = new UserId(Guid.NewGuid());
        await ApiTestHelper.RegisterAreaAsync(_client, areaId, "Main Area", (Guid.NewGuid(), "Sink", 10));
        await SeedUserDirectoryAsync(userId, "123456", "Hanako", ManagedUserLifecycleStatus.Suspended);

        var etag = await ApiTestHelper.GetAreaEtagAsync(fixture, _client, areaId);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cleaning-areas/{areaId}/members");
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
        request.Content = JsonContent.Create(new
        {
            userId = userId.Value
        });

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        body!["error"]!["code"]!.GetValue<string>().Should().Be("ManagedUserNotActiveError");
    }

    private async Task SeedUserDirectoryAsync(
        UserId userId,
        string employeeNumber,
        string displayName,
        ManagedUserLifecycleStatus lifecycleStatus,
        string? departmentCode = "OPS")
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserDirectoryProjectionRepository>();
        await repository.UpsertAsync(
            new UserDirectoryProjection(
                userId,
                EmployeeNumber.Create(employeeNumber).Value,
                displayName,
                lifecycleStatus,
                departmentCode,
                1),
            1,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);
    }
}

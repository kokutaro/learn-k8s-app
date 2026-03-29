using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.WebApi.Endpoints.Support;

namespace OsoujiSystem.WebApi.Tests;

public sealed class ApiHttpResultsTests
{
    [Fact]
    public async Task FromMutationResultAsync_ShouldReturnSuccessAndReadyHeader_WhenWaitBypassed()
    {
        var accessor = new FakeConsistencyContextAccessor();
        accessor.Set(new ReadModelConsistencyToken(10));
        var waiter = new FakeVisibilityWaiter(new ReadModelVisibilityWaitResult(false, true, TimeSpan.FromMilliseconds(10)));
        var httpContext = CreateHttpContext();
        var response = httpContext.Response;

        var result = await ApiHttpResults.FromMutationResultAsync(
            ApplicationResult<string>.Success("ok"),
            response,
            waitEnabled: false,
            accessor,
            waiter,
            value => TypedResults.Ok(new ApiResponse<string>(value)),
            value => new ReadModelVisibilityPendingResponseBody(value, $"/api/v1/resources/{value}", "pending"),
            TestContext.Current.CancellationToken);

        await result.ExecuteAsync(httpContext);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Headers[ApiHttpResults.ReadModelVisibilityHeaderName].ToString().Should().Be(ApiHttpResults.ReadModelVisibilityReady);
        waiter.CallCount.Should().Be(0);
        accessor.TryGet(out _).Should().BeFalse();
    }

    [Fact]
    public async Task FromMutationResultAsync_ShouldReturnAcceptedAndPendingHeaders_WhenWaitTimesOut()
    {
        var accessor = new FakeConsistencyContextAccessor();
        accessor.Set(new ReadModelConsistencyToken(10));
        var waiter = new FakeVisibilityWaiter(new ReadModelVisibilityWaitResult(false, true, TimeSpan.FromMilliseconds(25)));
        var httpContext = CreateHttpContext();
        var response = httpContext.Response;

        var result = await ApiHttpResults.FromMutationResultAsync(
            ApplicationResult<long>.Success(7),
            response,
            waitEnabled: true,
            accessor,
            waiter,
            value => TypedResults.Ok(new ApiResponse<long>(value)),
            value => new ReadModelVisibilityPendingResponseBody(
                ResourceId: "resource-7",
                Location: "/api/v1/resources/resource-7",
                ReadModelStatus: "pending",
                Version: value),
            TestContext.Current.CancellationToken);

        await result.ExecuteAsync(httpContext);

        response.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        response.Headers[ApiHttpResults.ReadModelVisibilityHeaderName].ToString().Should().Be(ApiHttpResults.ReadModelVisibilityPending);
        response.Headers[ApiHttpResults.RetryAfterHeaderName].ToString().Should().Be("1");
        response.Headers.Location.ToString().Should().Be("/api/v1/resources/resource-7");
        response.Headers.ETag.ToString().Should().Be("\"7\"");
        waiter.CallCount.Should().Be(1);
        accessor.TryGet(out _).Should().BeFalse();
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
    }

    private sealed class FakeConsistencyContextAccessor : IReadModelConsistencyContextAccessor
    {
        private ReadModelConsistencyToken? _token;

        public bool TryGet(out ReadModelConsistencyToken token)
        {
            if (_token is { } value)
            {
                token = value;
                return true;
            }

            token = default;
            return false;
        }

        public void Set(ReadModelConsistencyToken token) => _token = token;

        public void Clear() => _token = null;
    }

    private sealed class FakeVisibilityWaiter(ReadModelVisibilityWaitResult result) : IReadModelVisibilityWaiter
    {
        public int CallCount { get; private set; }

        public Task<ReadModelVisibilityWaitResult> WaitUntilVisibleAsync(ReadModelConsistencyToken token, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }
}

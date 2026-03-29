using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Infrastructure.Persistence.Postgres;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class AsyncLocalReadModelConsistencyContextAccessorTests
{
    [Fact]
    public async Task Set_ShouldRemainVisibleAfterAwait_WhenContextInitializedBeforeAwait()
    {
        var accessor = new AsyncLocalReadModelConsistencyContextAccessor();
        accessor.Set(new ReadModelConsistencyToken(17));

        await Task.Yield();

        accessor.TryGet(out var token).Should().BeTrue();
        token.RequiredGlobalPosition.Should().Be(17);
    }

    [Fact]
    public void Set_ShouldRemainVisibleViaHttpContext_WhenRequestContextExists()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        var accessor = new AsyncLocalReadModelConsistencyContextAccessor(httpContextAccessor);
        accessor.Set(new ReadModelConsistencyToken(29));

        accessor.TryGet(out var token).Should().BeTrue();
        token.RequiredGlobalPosition.Should().Be(29);
    }

    [Fact]
    public void Clear_ShouldRemoveToken()
    {
        var accessor = new AsyncLocalReadModelConsistencyContextAccessor();
        accessor.Set(new ReadModelConsistencyToken(23));

        accessor.Clear();

        accessor.TryGet(out _).Should().BeFalse();
    }
}

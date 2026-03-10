using Microsoft.AspNetCore.Http;
using OsoujiSystem.Application.Abstractions;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class AsyncLocalReadModelConsistencyContextAccessor(IHttpContextAccessor? httpContextAccessor = null)
    : IReadModelConsistencyContextAccessor
{
    private static readonly object HttpContextItemKey = new();

    private readonly IHttpContextAccessor httpContextAccessor = httpContextAccessor ?? new HttpContextAccessor();
    private readonly AsyncLocal<ReadModelConsistencyToken?> _context = new();

    public bool TryGet(out ReadModelConsistencyToken token)
    {
        if (TryGetFromHttpContext(out token))
        {
            return true;
        }

        if (_context.Value is { } current)
        {
            token = current;
            return true;
        }

        token = default;
        return false;
    }

    public void Set(ReadModelConsistencyToken token)
    {
        _context.Value = token;

        var items = httpContextAccessor.HttpContext?.Items;
        if (items is not null)
        {
            items[HttpContextItemKey] = token;
        }
    }

    public void Clear()
    {
        _context.Value = null;

        var items = httpContextAccessor.HttpContext?.Items;
        items?.Remove(HttpContextItemKey);
    }

    private bool TryGetFromHttpContext(out ReadModelConsistencyToken token)
    {
        var items = httpContextAccessor.HttpContext?.Items;
        if (items is not null
            && items.TryGetValue(HttpContextItemKey, out var current)
            && current is ReadModelConsistencyToken value)
        {
            token = value;
            return true;
        }

        token = default;
        return false;
    }
}

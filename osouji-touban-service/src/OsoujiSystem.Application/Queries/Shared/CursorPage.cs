namespace OsoujiSystem.Application.Queries.Shared;

public sealed record CursorPage<T>(
    IReadOnlyList<T> Items,
    int Limit,
    bool HasNext,
    string? NextCursor);

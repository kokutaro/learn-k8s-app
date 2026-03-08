namespace OsoujiSystem.WebApi.Endpoints.Support;

internal sealed record ApiResponse<T>(T Data);

internal sealed record CursorPageResponse<T>(
    IReadOnlyList<T> Data,
    CursorPageMeta Meta,
    CursorPageLinks Links);

internal sealed record CursorPageMeta(
    int Limit,
    bool HasNext,
    string? NextCursor);

internal sealed record CursorPageLinks(string Self);

internal sealed record ApiErrorResponse(ApiErrorBody Error);

internal sealed record ApiErrorBody(
    string Code,
    string Message,
    IReadOnlyList<ApiErrorDetail>? Details = null,
    IReadOnlyDictionary<string, object?>? Args = null);

internal sealed record ApiErrorDetail(
    string? Field,
    string Message,
    string? Code = null);

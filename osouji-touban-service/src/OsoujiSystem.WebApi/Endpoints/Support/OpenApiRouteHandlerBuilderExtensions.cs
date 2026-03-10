namespace OsoujiSystem.WebApi.Endpoints.Support;

internal static class OpenApiRouteHandlerBuilderExtensions
{
    public static RouteHandlerBuilder ProducesApiError(this RouteHandlerBuilder builder, int statusCode)
        => builder.Produces<ApiErrorResponse>(statusCode, contentType: "application/json");

    public static RouteHandlerBuilder ProducesReadModelVisibilityPending(this RouteHandlerBuilder builder)
        => builder.Produces<ApiResponse<ReadModelVisibilityPendingResponseBody>>(
            StatusCodes.Status202Accepted,
            contentType: "application/json");
}

namespace OsoujiSystem.WebApi.Endpoints.Support;

internal static class OpenApiRouteHandlerBuilderExtensions
{
    extension(RouteHandlerBuilder builder)
    {
        public RouteHandlerBuilder ProducesApiError(int statusCode)
            => builder.Produces<ApiErrorResponse>(statusCode, contentType: "application/json");

        public RouteHandlerBuilder ProducesReadModelVisibilityPending()
            => builder.Produces<ApiResponse<ReadModelVisibilityPendingResponseBody>>(
                StatusCodes.Status202Accepted,
                contentType: "application/json");
    }
}

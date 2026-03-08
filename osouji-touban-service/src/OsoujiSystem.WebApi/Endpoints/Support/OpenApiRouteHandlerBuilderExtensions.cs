namespace OsoujiSystem.WebApi.Endpoints.Support;

internal static class OpenApiRouteHandlerBuilderExtensions
{
    public static RouteHandlerBuilder ProducesApiError(this RouteHandlerBuilder builder, int statusCode)
        => builder.Produces<ApiErrorResponse>(statusCode, contentType: "application/json");
}

using OsoujiSystem.WebApi.Endpoints.CleaningAreas;
using OsoujiSystem.WebApi.Endpoints.Facilities;
using OsoujiSystem.WebApi.Endpoints.Internal;
using OsoujiSystem.WebApi.Endpoints.Users;
using OsoujiSystem.WebApi.Endpoints.WeeklyDutyPlans;

namespace OsoujiSystem.WebApi.Endpoints;

internal static class OsoujiApiEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapOsoujiApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapFacilityEndpoints()
            .MapCleaningAreaEndpoints()
            .MapWeeklyDutyPlanEndpoints()
            .MapUserManagementEndpoints()
            .MapInternalEndpoints();

        return app;
    }
}

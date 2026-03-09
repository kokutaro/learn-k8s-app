using Cortex.Mediator;
using Microsoft.Extensions.Options;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.Queries.Facilities;
using OsoujiSystem.Application.Queries.Shared;
using OsoujiSystem.Application.UseCases.Facilities;
using OsoujiSystem.Domain.Entities.Facilities;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Infrastructure.Options;
using OsoujiSystem.WebApi.Endpoints.Support;

namespace OsoujiSystem.WebApi.Endpoints.Facilities;

internal static class FacilityEndpoints
{
    public static IEndpointRouteBuilder MapFacilityEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/facilities").WithTags("Facilities");

        group.MapGet("/", ListFacilitiesAsync)
            .Produces<CursorPageResponse<FacilitySummaryResponse>>()
            .ProducesApiError(StatusCodes.Status400BadRequest);
        group.MapGet("/{facilityId:guid}", GetFacilityAsync)
            .WithName("GetFacility")
            .Produces<ApiResponse<FacilityDetailResponse>>()
            .ProducesApiError(StatusCodes.Status404NotFound);
        group.MapPost("/", RegisterFacilityAsync)
            .Produces<ApiResponse<RegisterFacilityResponseBody>>(StatusCodes.Status201Created)
            .ProducesReadModelVisibilityPending()
            .ProducesApiError(StatusCodes.Status400BadRequest)
            .ProducesApiError(StatusCodes.Status409Conflict)
            .ProducesApiError(StatusCodes.Status500InternalServerError);
        group.MapPut("/{facilityId:guid}", UpdateFacilityAsync)
            .Produces<ApiResponse<FacilityVersionResponseBody>>()
            .ProducesReadModelVisibilityPending()
            .ProducesApiError(StatusCodes.Status400BadRequest)
            .ProducesApiError(StatusCodes.Status404NotFound)
            .ProducesApiError(StatusCodes.Status409Conflict)
            .ProducesApiError(StatusCodes.Status500InternalServerError);
        group.MapPut("/{facilityId:guid}/activation", ChangeFacilityActivationAsync)
            .Produces<ApiResponse<FacilityActivationResponseBody>>()
            .ProducesReadModelVisibilityPending()
            .ProducesApiError(StatusCodes.Status400BadRequest)
            .ProducesApiError(StatusCodes.Status404NotFound)
            .ProducesApiError(StatusCodes.Status409Conflict)
            .ProducesApiError(StatusCodes.Status500InternalServerError);

        return api;
    }

    private static async Task<IResult> ListFacilitiesAsync(
        HttpRequest request,
        IMediator mediator,
        string? query,
        string? status,
        string? cursor,
        int? limit,
        string? sort,
        CancellationToken ct)
    {
        FacilityLifecycleStatus? lifecycleStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseLifecycleStatus(status, out var parsedStatus))
            {
                return ApiHttpResults.Validation("status", "Supported values are active and inactive.");
            }

            lifecycleStatus = parsedStatus;
        }

        var sortOrder = (sort ?? "name").ToLowerInvariant() switch
        {
            "name" => FacilitySortOrder.NameAsc,
            "-name" => FacilitySortOrder.NameDesc,
            "facilitycode" => FacilitySortOrder.FacilityCodeAsc,
            "-facilitycode" => FacilitySortOrder.FacilityCodeDesc,
            _ => (FacilitySortOrder?)null
        };

        if (sortOrder is null)
        {
            return ApiHttpResults.Validation("sort", "Supported values are name, -name, facilityCode, and -facilityCode.");
        }

        var pageSize = Math.Clamp(limit ?? 20, 1, 100);
        var page = await mediator.QueryAsync(
            new ListFacilitiesQuery(query, lifecycleStatus, cursor, pageSize, sortOrder.Value),
            ct);

        return TypedResults.Ok(
            new CursorPageResponse<FacilitySummaryResponse>(
                page.Items.Select(ToFacilitySummary).ToArray(),
                new CursorPageMeta(page.Limit, page.HasNext, page.NextCursor),
                new CursorPageLinks(request.Path + request.QueryString.ToUriComponent())));
    }

    private static async Task<IResult> GetFacilityAsync(
        HttpResponse response,
        IMediator mediator,
        Guid facilityId,
        CancellationToken ct)
    {
        var facility = await mediator.QueryAsync(new GetFacilityQuery(facilityId), ct);
        if (facility is null)
        {
            return ApiHttpResults.FromError(new("NotFound", "Facility was not found.", new Dictionary<string, object?>
            {
                ["resource"] = "Facility",
                ["key"] = "facilityId",
                ["value"] = facilityId.ToString("D")
            }));
        }

        response.Headers["ETag"] = ApiHttpResults.ToEtag(new AggregateVersion(facility.Version));
        return TypedResults.Ok(new ApiResponse<FacilityDetailResponse>(ToFacilityDetail(facility)));
    }

    private static async Task<IResult> RegisterFacilityAsync(
        HttpResponse response,
        LinkGenerator links,
        IMediator mediator,
        IReadModelConsistencyContextAccessor consistencyContextAccessor,
        IReadModelVisibilityWaiter visibilityWaiter,
        IOptions<InfrastructureOptions> infrastructureOptions,
        RegisterFacilityBody? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return ApiHttpResults.Validation("body", "Request body is required.");
        }

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body.FacilityCode))
        {
            errors["facilityCode"] = ["FacilityCode is required."];
        }

        if (string.IsNullOrWhiteSpace(body.Name))
        {
            errors["name"] = ["Name is required."];
        }

        if (string.IsNullOrWhiteSpace(body.TimeZoneId))
        {
            errors["timeZoneId"] = ["TimeZoneId is required."];
        }

        if (errors.Count > 0)
        {
            return ApiHttpResults.Validation(errors);
        }

        var result = await mediator.SendAsync(new RegisterFacilityRequest
        {
            FacilityCode = body.FacilityCode!,
            FacilityName = body.Name!,
            Description = body.Description,
            TimeZoneId = body.TimeZoneId!
        }, ct);

        return await ApiHttpResults.FromMutationResultAsync(
            result,
            response,
            infrastructureOptions.Value.ProjectionVisibility.Enabled,
            consistencyContextAccessor,
            visibilityWaiter,
            value =>
            {
                var location = links.GetPathByName("GetFacility", new { facilityId = value.FacilityId.Value })
                    ?? $"/api/v1/facilities/{value.FacilityId}";
                response.Headers["Location"] = location;
                return TypedResults.Created(
                    location,
                    new ApiResponse<RegisterFacilityResponseBody>(
                        new RegisterFacilityResponseBody(
                            value.FacilityId.ToString(),
                            value.FacilityCode.Value,
                            ToLifecycleStatusToken(value.LifecycleStatus))));
            },
            value =>
            {
                var location = links.GetPathByName("GetFacility", new { facilityId = value.FacilityId.Value })
                    ?? $"/api/v1/facilities/{value.FacilityId}";
                return new ReadModelVisibilityPendingResponseBody(
                    value.FacilityId.ToString(),
                    location,
                    ApiHttpResults.ReadModelVisibilityPending);
            },
            ct);
    }

    private static async Task<IResult> UpdateFacilityAsync(
        HttpRequest request,
        HttpResponse response,
        IFacilityRepository repository,
        IMediator mediator,
        IReadModelConsistencyContextAccessor consistencyContextAccessor,
        IReadModelVisibilityWaiter visibilityWaiter,
        IOptions<InfrastructureOptions> infrastructureOptions,
        Guid facilityId,
        UpdateFacilityBody? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return ApiHttpResults.Validation("body", "Request body is required.");
        }

        var loadResult = await LoadFacilityForWriteAsync(request, repository, facilityId, ct);
        if (loadResult.Result is not null)
        {
            return loadResult.Result;
        }

        var result = await mediator.SendAsync(new UpdateFacilityRequest
        {
            FacilityId = new FacilityId(facilityId),
            FacilityName = body.Name ?? string.Empty,
            Description = body.Description,
            TimeZoneId = body.TimeZoneId ?? string.Empty,
            ExpectedVersion = loadResult.Loaded!.Value.Version
        }, ct);

        return await ApiHttpResults.FromMutationResultAsync(
            result,
            response,
            infrastructureOptions.Value.ProjectionVisibility.Enabled,
            consistencyContextAccessor,
            visibilityWaiter,
            value =>
            {
                response.Headers["ETag"] = ApiHttpResults.ToEtag(new AggregateVersion(value.Version));
                return TypedResults.Ok(
                    new ApiResponse<FacilityVersionResponseBody>(
                        new FacilityVersionResponseBody(
                            value.FacilityId.ToString(),
                            value.Version)));
            },
            value => new ReadModelVisibilityPendingResponseBody(
                value.FacilityId.ToString(),
                $"/api/v1/facilities/{value.FacilityId}",
                ApiHttpResults.ReadModelVisibilityPending,
                value.Version),
            ct);
    }

    private static async Task<IResult> ChangeFacilityActivationAsync(
        HttpRequest request,
        HttpResponse response,
        IFacilityRepository repository,
        IMediator mediator,
        IReadModelConsistencyContextAccessor consistencyContextAccessor,
        IReadModelVisibilityWaiter visibilityWaiter,
        IOptions<InfrastructureOptions> infrastructureOptions,
        Guid facilityId,
        ChangeFacilityActivationBody? body,
        CancellationToken ct)
    {
        if (body is null || !TryParseLifecycleStatus(body.LifecycleStatus, out var lifecycleStatus))
        {
            return ApiHttpResults.Validation("lifecycleStatus", "Expected one of: active, inactive.");
        }

        var loadResult = await LoadFacilityForWriteAsync(request, repository, facilityId, ct);
        if (loadResult.Result is not null)
        {
            return loadResult.Result;
        }

        var result = await mediator.SendAsync(new ChangeFacilityActivationRequest
        {
            FacilityId = new FacilityId(facilityId),
            LifecycleStatus = lifecycleStatus,
            ExpectedVersion = loadResult.Loaded!.Value.Version
        }, ct);

        return await ApiHttpResults.FromMutationResultAsync(
            result,
            response,
            infrastructureOptions.Value.ProjectionVisibility.Enabled,
            consistencyContextAccessor,
            visibilityWaiter,
            value =>
            {
                response.Headers["ETag"] = ApiHttpResults.ToEtag(new AggregateVersion(value.Version));
                return TypedResults.Ok(
                    new ApiResponse<FacilityActivationResponseBody>(
                        new FacilityActivationResponseBody(
                            value.FacilityId.ToString(),
                            ToLifecycleStatusToken(value.LifecycleStatus),
                            value.Version)));
            },
            value => new ReadModelVisibilityPendingResponseBody(
                value.FacilityId.ToString(),
                $"/api/v1/facilities/{value.FacilityId}",
                ApiHttpResults.ReadModelVisibilityPending,
                value.Version),
            ct);
    }

    private static async Task<(LoadedAggregate<Facility>? Loaded, IResult? Result)> LoadFacilityForWriteAsync(
        HttpRequest request,
        IFacilityRepository repository,
        Guid facilityId,
        CancellationToken ct)
    {
        if (!ApiHttpResults.TryParseIfMatch(request, out var expectedVersion))
        {
            return (null, ApiHttpResults.Validation("If-Match", "A valid If-Match header is required."));
        }

        var loaded = await repository.FindByIdAsync(new FacilityId(facilityId), ct);
        if (loaded is null)
        {
            return (null, ApiHttpResults.FromError(new("NotFound", "Facility was not found.", new Dictionary<string, object?>
            {
                ["resource"] = "Facility",
                ["key"] = "facilityId",
                ["value"] = facilityId.ToString("D")
            })));
        }

        if (loaded.Value.Version != expectedVersion)
        {
            return (null, ApiHttpResults.FromError(new("RepositoryConcurrency", "The aggregate was updated by another transaction.", new Dictionary<string, object?>
            {
                ["resource"] = "Facility",
                ["key"] = "facilityId",
                ["expectedVersion"] = expectedVersion.Value,
                ["actualVersion"] = loaded.Value.Version.Value
            })));
        }

        return (loaded, null);
    }

    private static bool TryParseLifecycleStatus(string? raw, out FacilityLifecycleStatus status)
    {
        status = default;
        return raw?.Trim().ToLowerInvariant() switch
        {
            "active" => Assign(FacilityLifecycleStatus.Active, out status),
            "inactive" => Assign(FacilityLifecycleStatus.Inactive, out status),
            _ => false
        };
    }

    private static bool Assign<T>(T source, out T destination)
    {
        destination = source;
        return true;
    }

    private static FacilitySummaryResponse ToFacilitySummary(FacilityListItemReadModel facility)
        => new(
            facility.Id.ToString(),
            facility.FacilityCode,
            facility.Name,
            facility.TimeZoneId,
            facility.LifecycleStatus,
            facility.Version);

    private static FacilityDetailResponse ToFacilityDetail(FacilityDetailReadModel facility)
        => new(
            facility.Id.ToString(),
            facility.FacilityCode,
            facility.Name,
            facility.Description,
            facility.TimeZoneId,
            facility.LifecycleStatus,
            facility.Version);

    private static string ToLifecycleStatusToken(FacilityLifecycleStatus status)
        => status switch
        {
            FacilityLifecycleStatus.Active => "active",
            FacilityLifecycleStatus.Inactive => "inactive",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

    private sealed record RegisterFacilityBody(
        string? FacilityCode,
        string? Name,
        string? Description,
        string? TimeZoneId);

    private sealed record UpdateFacilityBody(
        string? Name,
        string? Description,
        string? TimeZoneId);

    private sealed record ChangeFacilityActivationBody(string? LifecycleStatus);

    internal sealed record FacilitySummaryResponse(
        string Id,
        string FacilityCode,
        string Name,
        string TimeZoneId,
        string LifecycleStatus,
        long Version);

    internal sealed record FacilityDetailResponse(
        string Id,
        string FacilityCode,
        string Name,
        string? Description,
        string TimeZoneId,
        string LifecycleStatus,
        long Version);

    internal sealed record RegisterFacilityResponseBody(
        string FacilityId,
        string FacilityCode,
        string LifecycleStatus);

    internal sealed record FacilityVersionResponseBody(
        string FacilityId,
        long Version);

    internal sealed record FacilityActivationResponseBody(
        string FacilityId,
        string LifecycleStatus,
        long Version);
}

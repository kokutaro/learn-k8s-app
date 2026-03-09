using Cortex.Mediator;
using OsoujiSystem.Application.Queries.Users;
using OsoujiSystem.Application.UseCases.UserManagement;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.UserManagement;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.WebApi.Endpoints.Support;
// ReSharper disable NotAccessedPositionalProperty.Global

namespace OsoujiSystem.WebApi.Endpoints.Users;

internal static class UserManagementEndpoints
{
    public static IEndpointRouteBuilder MapUserManagementEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/users").WithTags("Users");

        group.MapGet("/", ListUsersAsync)
            .Produces<CursorPageResponse<UserSummaryResponse>>()
            .ProducesApiError(StatusCodes.Status400BadRequest);
        group.MapGet("/{userId:guid}", GetUserAsync)
            .Produces<ApiResponse<UserDetailResponse>>()
            .ProducesApiError(StatusCodes.Status404NotFound);
        group.MapPost("/", RegisterUserAsync)
            .Produces<ApiResponse<RegisterUserResponseBody>>(StatusCodes.Status201Created)
            .ProducesApiError(StatusCodes.Status400BadRequest)
            .ProducesApiError(StatusCodes.Status409Conflict)
            .ProducesApiError(StatusCodes.Status500InternalServerError);
        group.MapPatch("/{userId:guid}", UpdateUserProfileAsync)
            .Produces<ApiResponse<UserVersionResponseBody>>()
            .ProducesApiError(StatusCodes.Status400BadRequest)
            .ProducesApiError(StatusCodes.Status404NotFound)
            .ProducesApiError(StatusCodes.Status409Conflict)
            .ProducesApiError(StatusCodes.Status500InternalServerError);
        group.MapPost("/{userId:guid}/lifecycle", ChangeUserLifecycleAsync)
            .Produces<ApiResponse<UserLifecycleResponseBody>>()
            .ProducesApiError(StatusCodes.Status400BadRequest)
            .ProducesApiError(StatusCodes.Status404NotFound)
            .ProducesApiError(StatusCodes.Status409Conflict)
            .ProducesApiError(StatusCodes.Status500InternalServerError);
        group.MapPost("/{userId:guid}/identity-links", LinkAuthIdentityAsync)
            .Produces<ApiResponse<UserVersionResponseBody>>()
            .ProducesApiError(StatusCodes.Status400BadRequest)
            .ProducesApiError(StatusCodes.Status404NotFound)
            .ProducesApiError(StatusCodes.Status409Conflict)
            .ProducesApiError(StatusCodes.Status500InternalServerError);

        return api;
    }

    private static async Task<IResult> ListUsersAsync(
        HttpRequest request,
        IMediator mediator,
        string? query,
        string? status,
        string? cursor,
        int? limit,
        string? sort,
        CancellationToken ct)
    {
        ManagedUserLifecycleStatus? lifecycleStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseLifecycleStatus(status, out var parsedStatus))
            {
                return ApiHttpResults.Validation("status", "Supported values are pendingActivation, active, suspended, and archived.");
            }

            lifecycleStatus = parsedStatus;
        }

        var sortOrder = (sort ?? "displayName").ToLowerInvariant() switch
        {
            "displayname" => UserSortOrder.DisplayNameAsc,
            "-displayname" => UserSortOrder.DisplayNameDesc,
            "employeenumber" => UserSortOrder.EmployeeNumberAsc,
            "-employeenumber" => UserSortOrder.EmployeeNumberDesc,
            _ => (UserSortOrder?)null
        };

        if (sortOrder is null)
        {
            return ApiHttpResults.Validation("sort", "Supported values are displayName, -displayName, employeeNumber, and -employeeNumber.");
        }

        var pageSize = Math.Clamp(limit ?? 20, 1, 100);
        var page = await mediator.QueryAsync(
            new ListUsersQuery(query, lifecycleStatus, cursor, pageSize, sortOrder.Value),
            ct);

        return TypedResults.Ok(
            new CursorPageResponse<UserSummaryResponse>(
                page.Items.Select(ToUserSummary).ToArray(),
                new CursorPageMeta(page.Limit, page.HasNext, page.NextCursor),
                new CursorPageLinks(request.Path + request.QueryString.ToUriComponent())));
    }

    private static async Task<IResult> GetUserAsync(
        HttpResponse response,
        IMediator mediator,
        Guid userId,
        CancellationToken ct)
    {
        var user = await mediator.QueryAsync(new GetUserQuery(userId), ct);
        if (user is null)
        {
            return ApiHttpResults.FromError(new("NotFound", "ManagedUser was not found.", new Dictionary<string, object?>
            {
                ["resource"] = "ManagedUser",
                ["key"] = "userId",
                ["value"] = userId.ToString("D")
            }));
        }

        response.Headers["ETag"] = ApiHttpResults.ToEtag(new AggregateVersion(user.Version));
        return TypedResults.Ok(new ApiResponse<UserDetailResponse>(ToUserDetail(user)));
    }

    private static async Task<IResult> RegisterUserAsync(
        HttpResponse response,
        IMediator mediator,
        RegisterUserBody? body,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (body is null)
        {
            return ApiHttpResults.Validation("body", "Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(body.EmployeeNumber))
        {
            errors["employeeNumber"] = ["EmployeeNumber is required."];
        }

        if (string.IsNullOrWhiteSpace(body.DisplayName))
        {
            errors["displayName"] = ["DisplayName is required."];
        }

        if (!TryParseRegistrationSource(body.RegistrationSource, out var registrationSource))
        {
            errors["registrationSource"] = ["Expected one of: adminPortal, hrImport, selfService, idpProvisioning."];
        }

        if (errors.Count > 0)
        {
            return ApiHttpResults.Validation(errors);
        }

        var result = await mediator.SendAsync(new RegisterUserRequest
        {
            EmployeeNumber = body.EmployeeNumber!,
            DisplayName = body.DisplayName!,
            EmailAddress = body.EmailAddress,
            DepartmentCode = body.DepartmentCode,
            RegistrationSource = registrationSource
        }, ct);

        return ApiHttpResults.FromApplicationResult(result, value =>
        {
            var location = $"/api/v1/users/{value.UserId:D}";
            response.Headers["Location"] = location;
            return TypedResults.Created(
                location,
                new ApiResponse<RegisterUserResponseBody>(
                    new RegisterUserResponseBody(
                        value.UserId,
                        value.EmployeeNumber,
                        value.LifecycleStatus)));
        });
    }

    private static async Task<IResult> UpdateUserProfileAsync(
        HttpRequest request,
        HttpResponse response,
        IManagedUserRepository repository,
        IMediator mediator,
        Guid userId,
        UpdateUserProfileBody? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return ApiHttpResults.Validation("body", "Request body is required.");
        }

        var loadResult = await LoadUserForWriteAsync(request, repository, userId, ct);
        if (loadResult.Result is not null)
        {
            return loadResult.Result;
        }

        var result = await mediator.SendAsync(new UpdateUserProfileRequest
        {
            UserId = new UserId(userId),
            DisplayName = body.DisplayName,
            EmailAddress = body.EmailAddress,
            DepartmentCode = body.DepartmentCode,
            ExpectedVersion = loadResult.Loaded!.Value.Version
        }, ct);

        return await ApiHttpResults.FromApplicationResultAsync(result, async value =>
        {
            response.Headers["ETag"] = $"\"{value.Version}\"";
            return await Task.FromResult(
                TypedResults.Ok(
                    new ApiResponse<UserVersionResponseBody>(
                        new UserVersionResponseBody(value.UserId, value.Version))));
        });
    }

    private static async Task<IResult> ChangeUserLifecycleAsync(
        HttpRequest request,
        HttpResponse response,
        IManagedUserRepository repository,
        IMediator mediator,
        Guid userId,
        ChangeUserLifecycleBody? body,
        CancellationToken ct)
    {
        if (body is null || !TryParseLifecycleStatus(body.LifecycleStatus, out var lifecycleStatus))
        {
            return ApiHttpResults.Validation("lifecycleStatus", "Expected one of: pendingActivation, active, suspended, archived.");
        }

        var loadResult = await LoadUserForWriteAsync(request, repository, userId, ct);
        if (loadResult.Result is not null)
        {
            return loadResult.Result;
        }

        var result = await mediator.SendAsync(new ChangeUserLifecycleRequest
        {
            UserId = new UserId(userId),
            LifecycleStatus = lifecycleStatus,
            ExpectedVersion = loadResult.Loaded!.Value.Version
        }, ct);

        return ApiHttpResults.FromApplicationResult(result, value =>
        {
            response.Headers["ETag"] = $"\"{value.Version}\"";
            return TypedResults.Ok(
                new ApiResponse<UserLifecycleResponseBody>(
                    new UserLifecycleResponseBody(
                        value.UserId,
                        value.LifecycleStatus,
                        value.Version)));
        });
    }

    private static async Task<IResult> LinkAuthIdentityAsync(
        HttpRequest request,
        HttpResponse response,
        IManagedUserRepository repository,
        IMediator mediator,
        Guid userId,
        LinkAuthIdentityBody? body,
        CancellationToken ct)
    {
        if (body is null)
        {
            return ApiHttpResults.Validation("body", "Request body is required.");
        }

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body.IdentityProviderKey))
        {
            errors["identityProviderKey"] = ["IdentityProviderKey is required."];
        }

        if (string.IsNullOrWhiteSpace(body.IdentitySubject))
        {
            errors["identitySubject"] = ["IdentitySubject is required."];
        }

        if (errors.Count > 0)
        {
            return ApiHttpResults.Validation(errors);
        }

        var loadResult = await LoadUserForWriteAsync(request, repository, userId, ct);
        if (loadResult.Result is not null)
        {
            return loadResult.Result;
        }

        var result = await mediator.SendAsync(new LinkAuthIdentityRequest
        {
            UserId = new UserId(userId),
            IdentityProviderKey = body.IdentityProviderKey!,
            IdentitySubject = body.IdentitySubject!,
            LoginHint = body.LoginHint,
            ExpectedVersion = loadResult.Loaded!.Value.Version
        }, ct);

        return ApiHttpResults.FromApplicationResult(result, value =>
        {
            response.Headers["ETag"] = $"\"{value.Version}\"";
            return TypedResults.Ok(
                new ApiResponse<UserVersionResponseBody>(
                    new UserVersionResponseBody(value.UserId, value.Version)));
        });
    }

    private static async Task<(LoadedAggregate<ManagedUser>? Loaded, IResult? Result)> LoadUserForWriteAsync(
        HttpRequest request,
        IManagedUserRepository repository,
        Guid userId,
        CancellationToken ct)
    {
        if (!ApiHttpResults.TryParseIfMatch(request, out var expectedVersion))
        {
            return (null, ApiHttpResults.Validation("If-Match", "A valid If-Match header is required."));
        }

        var loaded = await repository.FindByIdAsync(new UserId(userId), ct);
        if (loaded is null)
        {
            return (null, ApiHttpResults.FromError(new("NotFound", "ManagedUser was not found.", new Dictionary<string, object?>
            {
                ["resource"] = "ManagedUser",
                ["key"] = "userId",
                ["value"] = userId.ToString("D")
            })));
        }

        if (loaded.Value.Version != expectedVersion)
        {
            return (null, ApiHttpResults.FromError(new("RepositoryConcurrency", "The aggregate was updated by another transaction.", new Dictionary<string, object?>
            {
                ["resource"] = "ManagedUser",
                ["key"] = "userId",
                ["expectedVersion"] = expectedVersion.Value,
                ["actualVersion"] = loaded.Value.Version.Value
            })));
        }

        return (loaded, null);
    }

    private static bool TryParseRegistrationSource(string? raw, out RegistrationSource registrationSource)
    {
        registrationSource = default;
        return raw?.Trim() switch
        {
            "adminPortal" => Assign(RegistrationSource.AdminPortal, out registrationSource),
            "hrImport" => Assign(RegistrationSource.HrImport, out registrationSource),
            "selfService" => Assign(RegistrationSource.SelfService, out registrationSource),
            "idpProvisioning" => Assign(RegistrationSource.IdpProvisioning, out registrationSource),
            _ => false
        };
    }

    private static bool TryParseLifecycleStatus(string? raw, out ManagedUserLifecycleStatus lifecycleStatus)
    {
        lifecycleStatus = default;
        return raw?.Trim() switch
        {
            "pendingActivation" => Assign(ManagedUserLifecycleStatus.PendingActivation, out lifecycleStatus),
            "active" => Assign(ManagedUserLifecycleStatus.Active, out lifecycleStatus),
            "suspended" => Assign(ManagedUserLifecycleStatus.Suspended, out lifecycleStatus),
            "archived" => Assign(ManagedUserLifecycleStatus.Archived, out lifecycleStatus),
            _ => false
        };
    }

    private static bool Assign<T>(T value, out T destination)
    {
        destination = value;
        return true;
    }

    private static UserSummaryResponse ToUserSummary(UserListItemReadModel user)
        => new(
            user.Id,
            user.EmployeeNumber,
            user.DisplayName,
            ToLifecycleStatusToken(user.LifecycleStatus),
            user.DepartmentCode,
            user.Version);

    private static UserDetailResponse ToUserDetail(UserDetailReadModel user)
        => new(
            user.Id,
            user.EmployeeNumber,
            user.DisplayName,
            user.EmailAddress,
            user.DepartmentCode,
            ToLifecycleStatusToken(user.LifecycleStatus),
            user.Version);

    private static string ToLifecycleStatusToken(string lifecycleStatus)
        => lifecycleStatus.Trim().ToLowerInvariant() switch
        {
            "pendingactivation" => "pendingActivation",
            "active" => "active",
            "suspended" => "suspended",
            "archived" => "archived",
            _ => lifecycleStatus
        };

    private static string ToLifecycleStatusToken(ManagedUserLifecycleStatus lifecycleStatus)
        => lifecycleStatus switch
        {
            ManagedUserLifecycleStatus.PendingActivation => "pendingActivation",
            ManagedUserLifecycleStatus.Active => "active",
            ManagedUserLifecycleStatus.Suspended => "suspended",
            ManagedUserLifecycleStatus.Archived => "archived",
            _ => lifecycleStatus.ToString()
        };

    private sealed record RegisterUserBody(
        string? EmployeeNumber,
        string? DisplayName,
        string? EmailAddress,
        string? DepartmentCode,
        string? RegistrationSource);

    private sealed record UpdateUserProfileBody(
        string? DisplayName,
        string? EmailAddress,
        string? DepartmentCode);

    private sealed record ChangeUserLifecycleBody(string? LifecycleStatus);

    private sealed record LinkAuthIdentityBody(
        string? IdentityProviderKey,
        string? IdentitySubject,
        string? LoginHint);

    internal sealed record RegisterUserResponseBody(
        Guid UserId,
        string EmployeeNumber,
        string LifecycleStatus);

    internal sealed record UserVersionResponseBody(
        Guid UserId,
        long Version);

    internal sealed record UserLifecycleResponseBody(
        Guid UserId,
        string LifecycleStatus,
        long Version);

    internal sealed record UserSummaryResponse(
        Guid UserId,
        string EmployeeNumber,
        string DisplayName,
        string LifecycleStatus,
        string? DepartmentCode,
        long Version);

    internal sealed record UserDetailResponse(
        Guid UserId,
        string EmployeeNumber,
        string DisplayName,
        string? EmailAddress,
        string? DepartmentCode,
        string LifecycleStatus,
        long Version);
}

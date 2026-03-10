using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Infrastructure.Observability;

namespace OsoujiSystem.WebApi.Endpoints.Support;

internal static class ApiHttpResults
{
    internal const string ReadModelVisibilityHeaderName = "X-ReadModel-Visibility";
    internal const string ReadModelVisibilityReady = "ready";
    internal const string ReadModelVisibilityPending = "pending";
    internal const string RetryAfterHeaderName = "Retry-After";

    public static IResult FromApplicationResult<T>(ApplicationResult<T> result, Func<T, IResult> onSuccess)
    {
        return result.IsSuccess
            ? onSuccess(result.Value)
            : FromError(result.Error);
    }

    public static Task<IResult> FromApplicationResultAsync<T>(ApplicationResult<T> result, Func<T, Task<IResult>> onSuccess)
    {
        return result.IsSuccess
            ? onSuccess(result.Value)
            : Task.FromResult(FromError(result.Error));
    }

    public static Task<IResult> FromMutationResultAsync<T>(
        ApplicationResult<T> result,
        HttpResponse response,
        bool waitEnabled,
        IReadModelConsistencyContextAccessor consistencyContextAccessor,
        IReadModelVisibilityWaiter visibilityWaiter,
        Func<T, IResult> onSuccess,
        Func<T, ReadModelVisibilityPendingResponseBody> onPending,
        CancellationToken ct)
        => FromMutationResultAsync(
            result,
            response,
            waitEnabled,
            consistencyContextAccessor,
            visibilityWaiter,
            value => Task.FromResult(onSuccess(value)),
            onPending,
            ct);

    public static async Task<IResult> FromMutationResultAsync<T>(
        ApplicationResult<T> result,
        HttpResponse response,
        bool waitEnabled,
        IReadModelConsistencyContextAccessor consistencyContextAccessor,
        IReadModelVisibilityWaiter visibilityWaiter,
        Func<T, Task<IResult>> onSuccess,
        Func<T, ReadModelVisibilityPendingResponseBody> onPending,
        CancellationToken ct)
    {
        if (result.IsFailure)
        {
            return FromError(result.Error);
        }

        try
        {
            if (!waitEnabled || !consistencyContextAccessor.TryGet(out var token))
            {
                RecordReadModelVisibilityWait(response, "bypass", TimeSpan.Zero);
                response.Headers[ReadModelVisibilityHeaderName] = ReadModelVisibilityReady;
                return await onSuccess(result.Value);
            }

            var waitResult = await visibilityWaiter.WaitUntilVisibleAsync(token, ct);
            if (waitResult.IsVisible)
            {
                RecordReadModelVisibilityWait(response, "visible", waitResult.Waited);
                response.Headers[ReadModelVisibilityHeaderName] = ReadModelVisibilityReady;
                return await onSuccess(result.Value);
            }

            RecordReadModelVisibilityWait(response, "timeout", waitResult.Waited);
            return AcceptedReadModelVisibilityPending(response, onPending(result.Value));
        }
        finally
        {
            consistencyContextAccessor.Clear();
        }
    }

    public static IResult AcceptedReadModelVisibilityPending(
        HttpResponse response,
        ReadModelVisibilityPendingResponseBody body)
    {
        response.Headers[RetryAfterHeaderName] = "1";
        response.Headers[ReadModelVisibilityHeaderName] = ReadModelVisibilityPending;
        response.Headers["Location"] = body.Location;

        if (body.Version is not null)
        {
            response.Headers["ETag"] = ToEtag(new AggregateVersion(body.Version.Value));
        }

        return TypedResults.Accepted(
            body.Location,
            new ApiResponse<ReadModelVisibilityPendingResponseBody>(body));
    }

    private static void RecordReadModelVisibilityWait(HttpResponse response, string result, TimeSpan waited)
    {
        var endpoint = response.HttpContext.Request.Path.Value ?? "unknown";
        KeyValuePair<string, object?>[] tags =
        [
            new("endpoint", endpoint),
            new("result", result)
        ];

        OsoujiTelemetry.ReadModelVisibilityWaitRequestsTotal.Add(1, tags);
        OsoujiTelemetry.ReadModelVisibilityWaitDurationSeconds.Record(waited.TotalSeconds, tags);
    }

    public static IResult FromError(ApplicationError error)
    {
        var errorResponse = new ApiErrorResponse(new ApiErrorBody(
            error.Code,
            error.Message,
            Args: error.Args));

        return error.Code switch
        {
            "NotFound" => TypedResults.NotFound(errorResponse),
            "InvalidWeekIdError" => TypedResults.BadRequest(errorResponse),
            "InvalidWeekRuleError" => TypedResults.BadRequest(errorResponse),
            "InvalidWeekRuleTimeZoneError" => TypedResults.BadRequest(errorResponse),
            "InvalidEmployeeNumberError" => TypedResults.BadRequest(errorResponse),
            "InvalidDisplayNameError" => TypedResults.BadRequest(errorResponse),
            "InvalidEmailAddressError" => TypedResults.BadRequest(errorResponse),
            "InvalidDepartmentCodeError" => TypedResults.BadRequest(errorResponse),
            "InvalidIdentityProviderKeyError" => TypedResults.BadRequest(errorResponse),
            "InvalidIdentitySubjectError" => TypedResults.BadRequest(errorResponse),
            "InvalidFacilityCodeError" => TypedResults.BadRequest(errorResponse),
            "InvalidFacilityNameError" => TypedResults.BadRequest(errorResponse),
            "InvalidFacilityDescriptionError" => TypedResults.BadRequest(errorResponse),
            "InvalidFacilityTimeZoneError" => TypedResults.BadRequest(errorResponse),
            "RepositoryConcurrency" => TypedResults.Conflict(errorResponse),
            "RepositoryDuplicate" => TypedResults.Conflict(errorResponse),
            "WeeklyPlanAlreadyExists" => TypedResults.Conflict(errorResponse),
            "DuplicateCleaningSpotError" => TypedResults.Conflict(errorResponse),
            "DuplicateAreaMemberError" => TypedResults.Conflict(errorResponse),
            "UserAlreadyAssignedToAnotherAreaError" => TypedResults.Conflict(errorResponse),
            "DuplicateEmployeeNumberError" => TypedResults.Conflict(errorResponse),
            "DuplicateFacilityCodeError" => TypedResults.Conflict(errorResponse),
            "DuplicateAuthIdentityLinkError" => TypedResults.Conflict(errorResponse),
            "ManagedUserAlreadyArchivedError" => TypedResults.Conflict(errorResponse),
            "ManagedUserNotActiveError" => TypedResults.Conflict(errorResponse),
            "FacilityNotActiveError" => TypedResults.Conflict(errorResponse),
            "WeekAlreadyClosedError" => TypedResults.Conflict(errorResponse),
            "InvalidTransferRequest" => TypedResults.Conflict(errorResponse),
            "CleaningAreaHasNoSpotError" => TypedResults.Conflict(errorResponse),
            "NoAvailableUserForSpotError" => TypedResults.Conflict(errorResponse),
            "InvalidRebalanceRequestError" => TypedResults.Conflict(errorResponse),
            _ => TypedResults.InternalServerError(errorResponse)
        };
    }

    public static IResult Validation(string field, string message)
        => Validation(new Dictionary<string, string[]>
        {
            [field] = [message]
        });

    public static IResult Validation(IDictionary<string, string[]> errors)
        => TypedResults.BadRequest(
            new ApiErrorResponse(
                new ApiErrorBody(
                    "ValidationError",
                    "Request validation failed.",
                    errors
                        .SelectMany(pair => pair.Value.Select(message => new ApiErrorDetail(pair.Key, message, "validation")))
                        .ToArray())));

    public static string ToEtag(AggregateVersion version) => $"\"{version.Value}\"";

    public static bool TryParseIfMatch(HttpRequest request, out AggregateVersion version)
    {
        version = default;

        if (!request.Headers.TryGetValue("If-Match", out var values))
        {
            return false;
        }

        var raw = values.ToString().Trim();
        if (raw.StartsWith('"') && raw.EndsWith('"') && raw.Length >= 2)
        {
            raw = raw[1..^1];
        }

        if (!long.TryParse(raw, out var parsed) || parsed < 1)
        {
            return false;
        }

        version = new AggregateVersion(parsed);
        return true;
    }
}

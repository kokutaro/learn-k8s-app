using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.WebApi.Endpoints.Support;

internal static class ApiHttpResults
{
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
            "Unexpected" => TypedResults.InternalServerError(errorResponse),
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

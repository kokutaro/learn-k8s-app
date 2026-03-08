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
        var statusCode = error.Code switch
        {
            "NotFound" => StatusCodes.Status404NotFound,
            "InvalidWeekIdError" => StatusCodes.Status400BadRequest,
            "InvalidWeekRuleError" => StatusCodes.Status400BadRequest,
            "InvalidWeekRuleTimeZoneError" => StatusCodes.Status400BadRequest,
            "InvalidEmployeeNumberError" => StatusCodes.Status400BadRequest,
            "InvalidDisplayNameError" => StatusCodes.Status400BadRequest,
            "InvalidEmailAddressError" => StatusCodes.Status400BadRequest,
            "InvalidDepartmentCodeError" => StatusCodes.Status400BadRequest,
            "InvalidIdentityProviderKeyError" => StatusCodes.Status400BadRequest,
            "InvalidIdentitySubjectError" => StatusCodes.Status400BadRequest,
            "RepositoryConcurrency" => StatusCodes.Status409Conflict,
            "RepositoryDuplicate" => StatusCodes.Status409Conflict,
            "WeeklyPlanAlreadyExists" => StatusCodes.Status409Conflict,
            "DuplicateCleaningSpotError" => StatusCodes.Status409Conflict,
            "DuplicateAreaMemberError" => StatusCodes.Status409Conflict,
            "UserAlreadyAssignedToAnotherAreaError" => StatusCodes.Status409Conflict,
            "DuplicateEmployeeNumberError" => StatusCodes.Status409Conflict,
            "DuplicateAuthIdentityLinkError" => StatusCodes.Status409Conflict,
            "ManagedUserAlreadyArchivedError" => StatusCodes.Status409Conflict,
            "ManagedUserNotActiveError" => StatusCodes.Status409Conflict,
            "WeekAlreadyClosedError" => StatusCodes.Status409Conflict,
            "InvalidTransferRequest" => StatusCodes.Status409Conflict,
            "CleaningAreaHasNoSpotError" => StatusCodes.Status409Conflict,
            "NoAvailableUserForSpotError" => StatusCodes.Status409Conflict,
            "InvalidRebalanceRequestError" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        return TypedResults.Json(
            new
            {
                error = new
                {
                    code = error.Code,
                    message = error.Message,
                    args = error.Args
                }
            },
            statusCode: statusCode);
    }

    public static IResult Validation(string field, string message)
        => TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = [message]
        });

    public static IResult Validation(IDictionary<string, string[]> errors)
        => TypedResults.ValidationProblem(errors);

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

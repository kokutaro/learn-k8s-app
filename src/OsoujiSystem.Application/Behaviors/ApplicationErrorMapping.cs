using System.Reflection;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Application.Behaviors;

internal static class ApplicationErrorMapping
{
    public static ApplicationError FromException(Exception exception)
    {
        return exception switch
        {
            ApplicationErrorException applicationErrorException => applicationErrorException.Error,
            RepositoryConcurrencyException concurrencyException => new ApplicationError(
                "RepositoryConcurrency",
                "The aggregate was updated by another transaction.",
                new Dictionary<string, object?> { ["detail"] = concurrencyException.Message }),
            RepositoryDuplicateException duplicateException => new ApplicationError(
                "RepositoryDuplicate",
                "A duplicate aggregate was detected.",
                new Dictionary<string, object?> { ["detail"] = duplicateException.Message }),
            _ => new ApplicationError(
                "Unexpected",
                "An unexpected application error occurred.",
                new Dictionary<string, object?>
                {
                    ["detail"] = exception.Message,
                    ["exceptionType"] = exception.GetType().FullName
                })
        };
    }

    public static TResponse CreateFailureResponse<TResponse>(ApplicationError error)
    {
        var responseType = typeof(TResponse);
        if (!responseType.IsGenericType || responseType.GetGenericTypeDefinition() != typeof(ApplicationResult<>))
        {
            throw new InvalidOperationException($"Response type '{responseType.FullName}' is not an ApplicationResult<T>.");
        }

        var failureMethod = responseType.GetMethod(
            nameof(ApplicationResult<object>.Failure),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(string), typeof(string), typeof(IReadOnlyDictionary<string, object?>)]);

        if (failureMethod is null)
        {
            throw new InvalidOperationException($"Response type '{responseType.FullName}' does not expose ApplicationResult.Failure.");
        }

        return (TResponse)failureMethod.Invoke(null, [error.Code, error.Message, error.Args])!;
    }
}

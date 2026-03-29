using OsoujiSystem.Domain.Errors;

namespace OsoujiSystem.Application.Abstractions;

public sealed record ApplicationError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?> Args);

public sealed class ApplicationResult<T>
{
    private ApplicationResult(T value)
    {
        Value = value;
        IsSuccess = true;
    }

    private ApplicationResult(ApplicationError error)
    {
        Error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T Value { get; } = default!;
    public ApplicationError Error { get; } = null!;

    public static ApplicationResult<T> Success(T value) => new(value);

    public static ApplicationResult<T> Failure(
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? args = null)
    {
        return new ApplicationResult<T>(new ApplicationError(code, message, args ?? new Dictionary<string, object?>()));
    }

    public static ApplicationResult<T> FromDomainError(DomainError error)
    {
        return new ApplicationResult<T>(new ApplicationError(error.Code, error.Message, error.Args));
    }
}

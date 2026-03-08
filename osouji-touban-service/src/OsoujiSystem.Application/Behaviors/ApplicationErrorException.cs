using OsoujiSystem.Application.Abstractions;

namespace OsoujiSystem.Application.Behaviors;

public sealed class ApplicationErrorException(ApplicationError error, Exception? innerException = null)
    : Exception(error.Message, innerException)
{
    public ApplicationError Error { get; } = error;
}

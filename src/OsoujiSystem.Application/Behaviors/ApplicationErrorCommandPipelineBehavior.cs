using Cortex.Mediator.Commands;
using Microsoft.Extensions.Logging;

namespace OsoujiSystem.Application.Behaviors;

public sealed class ApplicationErrorCommandPipelineBehavior<TCommand, TResponse>(
    ILogger<ApplicationErrorCommandPipelineBehavior<TCommand, TResponse>> logger)
    : ICommandPipelineBehavior<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<TResponse> Handle(
        TCommand command,
        CommandHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception exception)
        {
            var error = ApplicationErrorMapping.FromException(exception);

            logger.LogError(
                exception,
                "Mediator command {CommandType} failed with application error {ErrorCode}.",
                typeof(TCommand).Name,
                error.Code);

            return ApplicationErrorMapping.CreateFailureResponse<TResponse>(error);
        }
    }
}

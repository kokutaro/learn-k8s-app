using System.Diagnostics;
using Cortex.Mediator.Commands;

namespace OsoujiSystem.Application.Observability;

public sealed class TracingCommandBehavior<TCommand> : ICommandPipelineBehavior<TCommand>
    where TCommand : ICommand
{
    public async Task Handle(
        TCommand command,
        CommandHandlerDelegate next,
        CancellationToken cancellationToken)
    {
        using var activity = StartCommandActivity(command);

        try
        {
            await next();
        }
        catch (Exception ex)
        {
            MarkError(activity, ex);
            throw;
        }
    }

    private static Activity? StartCommandActivity(TCommand command)
    {
        var commandType = command.GetType();
        var activity = ApplicationTelemetry.ActivitySource.StartActivity(
            $"mediator.command {commandType.Name}",
            ActivityKind.Internal);

        PopulateCommonTags(activity, commandType, "mediator.request.type");
        return activity;
    }

    private static void PopulateCommonTags(Activity? activity, Type type, string typeTagName)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("messaging.system", "cortex.mediator");
        activity.SetTag("messaging.operation", "process");
        activity.SetTag(typeTagName, type.FullName ?? type.Name);
        activity.SetTag("code.namespace", type.Namespace);
        activity.SetTag("code.function", type.Name);
    }

    private static void MarkError(Activity? activity, Exception ex)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity.AddEvent(CreateExceptionEvent(ex));
    }

    private static ActivityEvent CreateExceptionEvent(Exception ex)
        => new(
            "exception",
            tags: new ActivityTagsCollection
            {
                { "exception.type", ex.GetType().FullName },
                { "exception.message", ex.Message },
                { "exception.stacktrace", ex.ToString() }
            });
}

public sealed class TracingCommandBehavior<TCommand, TResult> : ICommandPipelineBehavior<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<TResult> Handle(
        TCommand command,
        CommandHandlerDelegate<TResult> next,
        CancellationToken cancellationToken)
    {
        using var activity = StartCommandActivity(command);

        try
        {
            return await next();
        }
        catch (Exception ex)
        {
            MarkError(activity, ex);
            throw;
        }
    }

    private static Activity? StartCommandActivity(TCommand command)
    {
        var commandType = command.GetType();
        var activity = ApplicationTelemetry.ActivitySource.StartActivity(
            $"mediator.command {commandType.Name}",
            ActivityKind.Internal);

        PopulateCommonTags(activity, commandType);
        return activity;
    }

    private static void PopulateCommonTags(Activity? activity, Type type)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("messaging.system", "cortex.mediator");
        activity.SetTag("messaging.operation", "process");
        activity.SetTag("mediator.request.type", type.FullName ?? type.Name);
        activity.SetTag("code.namespace", type.Namespace);
        activity.SetTag("code.function", type.Name);
    }

    private static void MarkError(Activity? activity, Exception ex)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity.AddEvent(CreateExceptionEvent(ex));
    }

    private static ActivityEvent CreateExceptionEvent(Exception ex)
        => new(
            "exception",
            tags: new ActivityTagsCollection
            {
                { "exception.type", ex.GetType().FullName },
                { "exception.message", ex.Message },
                { "exception.stacktrace", ex.ToString() }
            });
}

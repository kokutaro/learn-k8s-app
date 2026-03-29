using System.Diagnostics;
using AwesomeAssertions;
using Cortex.Mediator;
using Cortex.Mediator.Commands;
using Cortex.Mediator.DependencyInjection;
using Cortex.Mediator.Notifications;
using Microsoft.Extensions.DependencyInjection;
using OsoujiSystem.Application.Observability;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class MediatorTracingPipelineTests
{
    [Fact]
    public async Task SendAsync_ShouldCreateCommandSpan()
    {
        using var recorder = new ActivityRecorder();
        await using var provider = CreateProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var response = await mediator.SendAsync(new SuccessfulCommand("ok"), TestContext.Current.CancellationToken);

        response.Should().Be("ok");
        recorder.ShouldContainSingleActivity("mediator.command SuccessfulCommand")
            .GetTagItem("mediator.request.type").Should().Be(typeof(SuccessfulCommand).FullName);
    }

    [Fact]
    public async Task PublishAsync_ShouldCreateNotificationSpan_AndNestedCommandSpan()
    {
        using var recorder = new ActivityRecorder();
        await using var provider = CreateProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var response = await mediator.SendAsync(new TriggerNotificationCommand(), TestContext.Current.CancellationToken);

        response.Should().Be("published");

        var commandActivity = recorder.ShouldContainSingleActivity("mediator.command TriggerNotificationCommand");
        var notificationActivity = recorder.ShouldContainSingleActivity("mediator.notification TestNotification");
        var nestedCommandActivity = recorder.ShouldContainSingleActivity("mediator.command NestedCommand");

        notificationActivity.ParentSpanId.Should().Be(commandActivity.SpanId);
        nestedCommandActivity.ParentSpanId.Should().Be(notificationActivity.SpanId);
    }

    [Fact]
    public async Task SendAsync_ShouldMarkCommandSpanAsError_WhenHandlerThrows()
    {
        using var recorder = new ActivityRecorder();
        await using var provider = CreateProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var action = async () => await mediator.SendAsync(new ThrowingCommand());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");

        var activity = recorder.ShouldContainSingleActivity("mediator.command ThrowingCommand");
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.Events.Any(x => x.Name == "exception").Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_ShouldMarkNotificationSpanAsError_WhenHandlerThrows()
    {
        using var recorder = new ActivityRecorder();
        await using var provider = CreateProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var action = async () => await mediator.PublishAsync(new ThrowingNotification());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("notification boom");

        var activity = recorder.ShouldContainSingleActivity("mediator.notification ThrowingNotification");
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.Events.Any(x => x.Name == "exception").Should().BeTrue();
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCortexMediator(
            [typeof(MediatorTracingPipelineTests)],
            options =>
            {
                options.AddOpenCommandPipelineBehavior(typeof(TracingCommandBehavior<>));
                options.AddOpenCommandPipelineBehavior(typeof(TracingCommandBehavior<,>));
                options.AddOpenNotificationPipelineBehavior(typeof(TracingNotificationBehavior<>));
            });

        return services.BuildServiceProvider();
    }

    public sealed record SuccessfulCommand(string Value) : ICommand<string>;

    public sealed class SuccessfulCommandHandler : ICommandHandler<SuccessfulCommand, string>
    {
        public Task<string> Handle(SuccessfulCommand command, CancellationToken cancellationToken)
            => Task.FromResult(command.Value);
    }

    public sealed record TriggerNotificationCommand : ICommand<string>;

    public sealed class TriggerNotificationCommandHandler(IMediator mediator)
        : ICommandHandler<TriggerNotificationCommand, string>
    {
        public async Task<string> Handle(TriggerNotificationCommand command, CancellationToken cancellationToken)
        {
            await mediator.PublishAsync(new TestNotification(), cancellationToken);
            return "published";
        }
    }

    public sealed record NestedCommand : ICommand<string>;

    public sealed class NestedCommandHandler : ICommandHandler<NestedCommand, string>
    {
        public Task<string> Handle(NestedCommand command, CancellationToken cancellationToken)
            => Task.FromResult("nested");
    }

    public sealed record ThrowingCommand : ICommand<string>;

    public sealed class ThrowingCommandHandler : ICommandHandler<ThrowingCommand, string>
    {
        public Task<string> Handle(ThrowingCommand command, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    public sealed record TestNotification : INotification;

    public sealed class TestNotificationHandler(IMediator mediator)
        : INotificationHandler<TestNotification>
    {
        public async Task Handle(TestNotification notification, CancellationToken cancellationToken)
        {
            await mediator.SendAsync(new NestedCommand(), cancellationToken);
        }
    }

    public sealed record ThrowingNotification : INotification;

    public sealed class ThrowingNotificationHandler : INotificationHandler<ThrowingNotification>
    {
        public Task Handle(ThrowingNotification notification, CancellationToken cancellationToken)
            => throw new InvalidOperationException("notification boom");
    }

    private sealed class ActivityRecorder : IDisposable
    {
        private readonly ActivityListener _listener;

        public ActivityRecorder()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == ApplicationTelemetry.ActivitySourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => Activities.Add(activity)
            };

            ActivitySource.AddActivityListener(_listener);
        }

        public List<Activity> Activities { get; } = [];

        public Activity ShouldContainSingleActivity(string displayName)
        {
            Activities.Should().ContainSingle(activity => activity.DisplayName == displayName);
            return Activities.Single(activity => activity.DisplayName == displayName);
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }
}

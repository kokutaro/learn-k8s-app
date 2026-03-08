using Cortex.Mediator.Commands;
using Cortex.Mediator.Notifications;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.Behaviors;
using OsoujiSystem.Application.DependencyInjection;
using OsoujiSystem.Domain.Repositories;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class MediatorPipelineBehaviorTests
{
    [Fact]
    public async Task CommandBehavior_ShouldConvertUnhandledException_ToUnexpectedApplicationError()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var behavior = new ApplicationErrorCommandPipelineBehavior<TestCommand, ApplicationResult<string>>(
            loggerFactory.CreateLogger<ApplicationErrorCommandPipelineBehavior<TestCommand, ApplicationResult<string>>>());

        var result = await behavior.Handle(
            new TestCommand(),
            () => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Unexpected");
        result.Error.Args["detail"].Should().Be("boom");
    }

    [Fact]
    public async Task CommandBehavior_ShouldConvertRepositoryConcurrencyException_ToApplicationError()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var behavior = new ApplicationErrorCommandPipelineBehavior<TestCommand, ApplicationResult<string>>(
            loggerFactory.CreateLogger<ApplicationErrorCommandPipelineBehavior<TestCommand, ApplicationResult<string>>>());

        var result = await behavior.Handle(
            new TestCommand(),
            () => throw new RepositoryConcurrencyException("stale version"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RepositoryConcurrency");
    }

    [Fact]
    public async Task NotificationBehavior_ShouldWrapUnhandledException_AsApplicationErrorException()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var behavior = new ApplicationErrorNotificationPipelineBehavior<TestNotification>(
            loggerFactory.CreateLogger<ApplicationErrorNotificationPipelineBehavior<TestNotification>>());

        var action = async () => await behavior.Handle(
            new TestNotification(),
            () => throw new InvalidOperationException("notification failed"),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ApplicationErrorException>();
        exception.Which.Error.Code.Should().Be("Unexpected");
        exception.Which.Error.Args["detail"].Should().Be("notification failed");
    }

    [Fact]
    public void AddOsoujiApplication_ShouldRegisterMediatorApplicationErrorBehaviors()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddOsoujiApplication();

        services.Any(descriptor => descriptor.ImplementationType == typeof(ApplicationErrorCommandPipelineBehavior<,>))
            .Should()
            .BeTrue();
        services.Any(descriptor => descriptor.ImplementationType == typeof(ApplicationErrorNotificationPipelineBehavior<>))
            .Should()
            .BeTrue();
    }

    private sealed record TestCommand : ICommand<ApplicationResult<string>>;

    private sealed record TestNotification : INotification;
}

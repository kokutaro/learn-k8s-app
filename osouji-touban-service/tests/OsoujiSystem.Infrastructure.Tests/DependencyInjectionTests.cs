using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.DependencyInjection;
using OsoujiSystem.Application.Notifications;
using OsoujiSystem.Application.Queries.Abstractions;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Infrastructure.DependencyInjection;
using OsoujiSystem.Infrastructure.Pii;
using OsoujiSystem.Infrastructure.Projection;

namespace OsoujiSystem.Infrastructure.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddOsoujiInfrastructure_ShouldUseStubImplementations_WhenStubMode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infrastructure:PersistenceMode"] = "Stub"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOsoujiApplication();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddOsoujiInfrastructure(configuration, new TestHostEnvironment());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFacilityRepository>().GetType().Name.Should().Contain("Stub");
        provider.GetRequiredService<ICleaningAreaRepository>().GetType().Name.Should().Contain("Stub");
        provider.GetRequiredService<IWeeklyDutyPlanRepository>().GetType().Name.Should().Contain("Stub");
        provider.GetRequiredService<IAssignmentHistoryRepository>().GetType().Name.Should().Contain("Stub");
        provider.GetRequiredService<IFacilityReadRepository>().GetType().Name.Should().Contain("Stub");
        provider.GetRequiredService<ICleaningAreaReadRepository>().GetType().Name.Should().Contain("Stub");
        provider.GetRequiredService<IWeeklyDutyPlanReadRepository>().GetType().Name.Should().Contain("Stub");
        provider.GetRequiredService<IApplicationTransaction>().GetType().Name.Should().Contain("Stub");
        provider.GetRequiredService<IReadModelConsistencyContextAccessor>().GetType().Name.Should().Contain("Noop");
        provider.GetRequiredService<IReadModelVisibilityWaiter>().GetType().Name.Should().Contain("Noop");
        provider.GetServices<IHostedService>().Any(x => x.GetType().Name == "MainProjectionWorker").Should().BeFalse();
    }

    [Fact]
    public void AddOsoujiInfrastructure_ShouldUseEventStoreImplementations_WhenEventStoreMode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infrastructure:PersistenceMode"] = "EventStore",
                ["ConnectionStrings:osouji-db"] = "Host=localhost;Database=osouji;Username=postgres;Password=postgres",
                ["ConnectionStrings:osouji-redis"] = "localhost:6379",
                ["ConnectionStrings:osouji-rabbitmq"] = "amqp://guest:guest@localhost:5672/"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOsoujiApplication();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddOsoujiInfrastructure(configuration, new TestHostEnvironment());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFacilityRepository>().GetType().Name.Should().Contain("EventStore");
        provider.GetRequiredService<ICleaningAreaRepository>().GetType().Name.Should().Contain("EventStore");
        provider.GetRequiredService<IWeeklyDutyPlanRepository>().GetType().Name.Should().Contain("EventStore");
        provider.GetRequiredService<IAssignmentHistoryRepository>().GetType().Name.Should().Contain("EventStore");
        provider.GetRequiredService<IFacilityReadRepository>().GetType().Name.Should().Contain("Cached");
        provider.GetRequiredService<ICleaningAreaReadRepository>().GetType().Name.Should().Contain("Cached");
        provider.GetRequiredService<IWeeklyDutyPlanReadRepository>().GetType().Name.Should().Contain("Cached");
        provider.GetRequiredService<IApplicationTransaction>().GetType().Name.Should().Contain("Npgsql");
        provider.GetRequiredService<IReadModelConsistencyContextAccessor>().GetType().Name.Should().Contain("AsyncLocal");
        provider.GetRequiredService<IReadModelVisibilityWaiter>().GetType().Name.Should().Contain("Postgres");
        provider.GetRequiredService<IReadModelCacheInvalidationTaskRepository>().Should().NotBeNull();
        provider.GetRequiredService<IReadModelVisibilityCheckpointRepository>().Should().NotBeNull();
        provider.GetRequiredService<IReadModelVisibilityCheckpointAdvancer>().Should().NotBeNull();
        provider.GetRequiredService<IDomainEventDispatcher>().GetType().Name.Should().Contain("Outbox");
        provider.GetRequiredService<INotificationDispatcher>().Should().NotBeNull();
        provider.GetRequiredService<IPiiAnonymizer>().Should().NotBeNull();
        provider.GetServices<IHostedService>().Any(x => x.GetType().Name == "MainProjectionWorker").Should().BeTrue();
        provider.GetServices<IHostedService>().Any(x => x.GetType().Name == "CacheInvalidationRecoveryWorker").Should().BeTrue();
        provider.GetServices<IHostedService>().Any(x => x.GetType().Name == "ReadModelCacheInvalidationRecoveryWorker").Should().BeTrue();
        provider.GetServices<IHostedService>().Any(x => x.GetType().Name == "OutboxPublisherWorker").Should().BeTrue();
        provider.GetServices<IHostedService>().Any(x => x.GetType().Name == "RabbitMqTopologyHostedService").Should().BeTrue();
        provider.GetServices<IHostedService>().Any(x => x.GetType().Name == "InfrastructureMetricsCollectorWorker").Should().BeTrue();
        provider.GetServices<IHostedService>().Any(x => x.GetType().Name == "NotificationConsumerWorker").Should().BeTrue();
        provider.GetServices<IHostedService>().Any(x => x.GetType().Name == "IntegrationConsumerWorker").Should().BeTrue();
        provider.GetServices<IHostedService>().Any(x => x.GetType().Name == "RetentionPurgeWorker").Should().BeTrue();
    }

    [Fact]
    public void AddOsoujiInfrastructure_ShouldThrow_WhenEventStoreModeWithoutRedisConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infrastructure:PersistenceMode"] = "EventStore",
                ["ConnectionStrings:osouji-db"] = "Host=localhost;Database=osouji;Username=postgres;Password=postgres"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOsoujiApplication();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());

        var action = () => services.AddOsoujiInfrastructure(configuration, new TestHostEnvironment());
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:osouji-redis*");
    }

    [Fact]
    public void AddOsoujiInfrastructure_ShouldThrow_WhenEventStoreModeWithoutRabbitMqConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infrastructure:PersistenceMode"] = "EventStore",
                ["ConnectionStrings:osouji-db"] = "Host=localhost;Database=osouji;Username=postgres;Password=postgres",
                ["ConnectionStrings:osouji-redis"] = "localhost:6379"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOsoujiApplication();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());

        var action = () => services.AddOsoujiInfrastructure(configuration, new TestHostEnvironment());
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:osouji-rabbitmq*");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

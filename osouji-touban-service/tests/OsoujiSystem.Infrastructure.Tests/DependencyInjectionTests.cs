using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Infrastructure.DependencyInjection;

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
        services.AddOsoujiInfrastructure(configuration, new TestHostEnvironment());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICleaningAreaRepository>().GetType().Name.Should().Contain("Stub");
        provider.GetRequiredService<IWeeklyDutyPlanRepository>().GetType().Name.Should().Contain("Stub");
        provider.GetRequiredService<IAssignmentHistoryRepository>().GetType().Name.Should().Contain("Stub");
        provider.GetRequiredService<IApplicationTransaction>().GetType().Name.Should().Contain("Stub");
    }

    [Fact]
    public void AddOsoujiInfrastructure_ShouldUseEventStoreImplementations_WhenEventStoreMode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infrastructure:PersistenceMode"] = "EventStore",
                ["Infrastructure:Postgres:ConnectionString"] = "Host=localhost;Database=osouji;Username=postgres;Password=postgres"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOsoujiInfrastructure(configuration, new TestHostEnvironment());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICleaningAreaRepository>().GetType().Name.Should().Contain("EventStore");
        provider.GetRequiredService<IWeeklyDutyPlanRepository>().GetType().Name.Should().Contain("EventStore");
        provider.GetRequiredService<IAssignmentHistoryRepository>().GetType().Name.Should().Contain("EventStore");
        provider.GetRequiredService<IApplicationTransaction>().GetType().Name.Should().Contain("Npgsql");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

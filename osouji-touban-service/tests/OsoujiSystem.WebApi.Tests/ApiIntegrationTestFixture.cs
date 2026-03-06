using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OsoujiSystem.Infrastructure.Migrations;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace OsoujiSystem.WebApi.Tests;

public sealed class ApiIntegrationTestFixture : IAsyncLifetime
{
    private const string PostgresConnectionStringEnv = "INFRASTRUCTURE__POSTGRES__CONNECTIONSTRING";
    private const string RedisConnectionStringEnv = "INFRASTRUCTURE__REDIS__CONNECTIONSTRING";
    private const string RabbitHostEnv = "INFRASTRUCTURE__RABBITMQ__HOST";
    private const string PersistenceModeConfigEnv = "Infrastructure__PersistenceMode";
    private const string RabbitPortConfigEnv = "Infrastructure__RabbitMq__Port";
    private const string RabbitVirtualHostConfigEnv = "Infrastructure__RabbitMq__VirtualHost";
    private const string RabbitUsernameConfigEnv = "Infrastructure__RabbitMq__Username";
    private const string RabbitPasswordConfigEnv = "Infrastructure__RabbitMq__Password";
    private const string RabbitUseTlsConfigEnv = "Infrastructure__RabbitMq__UseTls";

    private static readonly string[] TruncateStatements =
    [
        "TRUNCATE TABLE data_retention_purge_reports RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE consumer_processed_events RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE cache_invalidation_tasks RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE projection_user_weekly_workloads RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE projection_weekly_plan_offduty RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE projection_weekly_plan_assignments RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE projection_weekly_plans RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE projection_cleaning_area_spots RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE projection_area_members RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE projection_cleaning_areas RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE projection_checkpoints RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE outbox_messages RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE event_store_snapshots RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE event_store_events RESTART IDENTITY CASCADE;"
    ];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("osouji_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7.4-alpine")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:4.1-management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private ConnectionMultiplexer? _redisConnection;
    private readonly Dictionary<string, string?> _previousEnvironment = [];

    public CustomWebApplicationFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        await _rabbitMq.StartAsync();

        var migration = DbMigrator.Migrate(_postgres.GetConnectionString());
        if (!migration.Successful)
        {
            throw migration.Error ?? new InvalidOperationException("Failed to migrate the test database.");
        }

        OverrideEnvironment(PostgresConnectionStringEnv, _postgres.GetConnectionString());
        OverrideEnvironment(RedisConnectionStringEnv, _redis.GetConnectionString());
        OverrideEnvironment(RabbitHostEnv, _rabbitMq.Hostname);
        OverrideEnvironment(PersistenceModeConfigEnv, "EventStore");
        OverrideEnvironment(RabbitPortConfigEnv, _rabbitMq.GetMappedPublicPort(5672).ToString());
        OverrideEnvironment(RabbitVirtualHostConfigEnv, "/");
        OverrideEnvironment(RabbitUsernameConfigEnv, "guest");
        OverrideEnvironment(RabbitPasswordConfigEnv, "guest");
        OverrideEnvironment(RabbitUseTlsConfigEnv, "false");

        _redisConnection = await ConnectionMultiplexer.ConnectAsync($"{_redis.GetConnectionString()},allowAdmin=true");
        Factory = new CustomWebApplicationFactory(BuildSettings());
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();

        if (_redisConnection is not null)
        {
            await _redisConnection.DisposeAsync();
        }

        RestoreEnvironment();

        await _rabbitMq.DisposeAsync();
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public HttpClient CreateClient()
        => Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        foreach (var statement in TruncateStatements)
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO projection_checkpoints (projector_name, last_global_position, updated_at)
            VALUES ('main_projector', 0, now())
            ON CONFLICT (projector_name) DO NOTHING;
            """,
            connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        ArgumentNullException.ThrowIfNull(_redisConnection);
        foreach (var endpoint in _redisConnection.GetEndPoints())
        {
            var server = _redisConnection.GetServer(endpoint);
            await server.FlushAllDatabasesAsync();
        }
    }

    private Dictionary<string, string?> BuildSettings()
    {
        return new Dictionary<string, string?>
        {
            ["Infrastructure:PersistenceMode"] = "EventStore",
            ["Infrastructure:Postgres:ConnectionString"] = _postgres.GetConnectionString(),
            ["Infrastructure:Redis:ConnectionString"] = _redis.GetConnectionString(),
            ["Infrastructure:RabbitMq:Host"] = _rabbitMq.Hostname,
            ["Infrastructure:RabbitMq:Port"] = _rabbitMq.GetMappedPublicPort(5672).ToString(),
            ["Infrastructure:RabbitMq:VirtualHost"] = "/",
            ["Infrastructure:RabbitMq:Username"] = "guest",
            ["Infrastructure:RabbitMq:Password"] = "guest",
            ["Infrastructure:RabbitMq:UseTls"] = "false"
        };
    }

    private void OverrideEnvironment(string name, string? value)
    {
        if (!_previousEnvironment.ContainsKey(name))
        {
            _previousEnvironment[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(name, value);
    }

    private void RestoreEnvironment()
    {
        foreach (var pair in _previousEnvironment)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    public sealed class CustomWebApplicationFactory(Dictionary<string, string?> settings)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(settings);
            });

            builder.ConfigureServices(services =>
            {
                var hostedServiceNames = new HashSet<string>(StringComparer.Ordinal)
                {
                    "OsoujiSystem.Infrastructure.Migrations.DevelopmentDbMigrationHostedService",
                    "OsoujiSystem.Infrastructure.Messaging.RabbitMqTopologyHostedService",
                    "OsoujiSystem.Infrastructure.Projection.MainProjectionWorker",
                    "OsoujiSystem.Infrastructure.Observability.InfrastructureMetricsCollectorWorker",
                    "OsoujiSystem.Infrastructure.Cache.CacheInvalidationRecoveryWorker",
                    "OsoujiSystem.Infrastructure.Outbox.OutboxPublisherWorker",
                    "OsoujiSystem.Infrastructure.Messaging.NotificationConsumerWorker",
                    "OsoujiSystem.Infrastructure.Messaging.IntegrationConsumerWorker",
                    "OsoujiSystem.Infrastructure.Retention.RetentionPurgeWorker"
                };

                var descriptors = services
                    .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
                    .Where(descriptor => hostedServiceNames.Contains(descriptor.ImplementationType?.FullName ?? string.Empty))
                    .ToArray();

                foreach (var descriptor in descriptors)
                {
                    services.Remove(descriptor);
                }
            });
        }
    }
}

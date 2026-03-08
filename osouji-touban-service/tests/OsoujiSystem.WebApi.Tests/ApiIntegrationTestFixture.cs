using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Infrastructure.Migrations;
using OsoujiSystem.Infrastructure.Projection;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace OsoujiSystem.WebApi.Tests;

public sealed class ApiIntegrationTestFixture : IAsyncLifetime
{
    public static readonly DateTimeOffset FixedUtcNow = new(2026, 03, 07, 00, 00, 00, TimeSpan.Zero);
    private static readonly Guid LegacyFacilityId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid LegacyFacilityEventId = Guid.Parse("00000000-0000-0000-0000-000000000101");

    private const string PostgresConnectionStringEnv = "ConnectionStrings__osouji-db";
    private const string RedisConnectionStringEnv = "ConnectionStrings__osouji-redis";
    private const string RabbitMqConnectionStringEnv = "ConnectionStrings__osouji-rabbitmq";
    private const string PersistenceModeConfigEnv = "Infrastructure__PersistenceMode";

    private static readonly string[] TruncateStatements =
    [
        "TRUNCATE TABLE data_retention_purge_reports RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE consumer_processed_events RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE cache_invalidation_tasks RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE projection_user_weekly_workloads RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE projection_weekly_plan_offduty RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE projection_weekly_plan_assignments RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE projection_weekly_plans RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE projection_user_directory RESTART IDENTITY CASCADE;",
        "TRUNCATE TABLE projection_facilities RESTART IDENTITY CASCADE;",
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
        OverrideEnvironment(RabbitMqConnectionStringEnv, GetRabbitMqConnectionString());
        OverrideEnvironment(PersistenceModeConfigEnv, "EventStore");

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

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO event_store_events (
                event_id,
                stream_id,
                stream_type,
                stream_version,
                event_type,
                event_schema_version,
                payload,
                metadata,
                occurred_at
            )
            VALUES (
                @eventId,
                @facilityId,
                'facility',
                1,
                'FacilityRegistered',
                1,
                CAST(@payload AS jsonb),
                '{}'::jsonb,
                @occurredAt
            );

            INSERT INTO event_store_snapshots (
                stream_id,
                stream_type,
                last_included_version,
                snapshot_payload,
                updated_at
            )
            VALUES (
                @facilityId,
                'facility',
                1,
                CAST(@snapshotPayload AS jsonb),
                now()
            );

            INSERT INTO projection_facilities (
                facility_id,
                facility_code,
                name,
                description,
                time_zone_id,
                lifecycle_status,
                source_event_id,
                aggregate_version,
                updated_at
            )
            VALUES (
                @facilityId,
                'LEGACY-DEFAULT',
                'Legacy Facility',
                'Backfilled facility for cleaning areas created before Facility BC existed.',
                'Asia/Tokyo',
                'Active',
                @eventId,
                1,
                now()
            );
            """,
            connection))
        {
            command.Parameters.AddWithValue("eventId", LegacyFacilityEventId);
            command.Parameters.AddWithValue("facilityId", LegacyFacilityId);
            command.Parameters.AddWithValue("occurredAt", FixedUtcNow.UtcDateTime);
            command.Parameters.AddWithValue("payload", """
            {"facilityId":"00000000-0000-0000-0000-000000000001","facilityCode":"LEGACY-DEFAULT","name":"Legacy Facility","description":"Backfilled facility for cleaning areas created before Facility BC existed.","timeZoneId":"Asia/Tokyo","lifecycleStatus":"Active"}
            """);
            command.Parameters.AddWithValue("snapshotPayload", """
            {"facilityCode":"LEGACY-DEFAULT","name":"Legacy Facility","description":"Backfilled facility for cleaning areas created before Facility BC existed.","timeZoneId":"Asia/Tokyo","lifecycleStatus":"Active"}
            """);
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
            ["ConnectionStrings:osouji-db"] = _postgres.GetConnectionString(),
            ["ConnectionStrings:osouji-redis"] = _redis.GetConnectionString(),
            ["ConnectionStrings:osouji-rabbitmq"] = GetRabbitMqConnectionString()
        };
    }

    private string GetRabbitMqConnectionString()
        => $"amqp://guest:guest@{_rabbitMq.Hostname}:{_rabbitMq.GetMappedPublicPort(5672)}/";

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

                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(new FrozenClock(FixedUtcNow));
            });
        }
    }

    public async Task DrainProjectionAsync(CancellationToken ct = default)
    {
        var projector = Factory.Services.GetRequiredService<MainProjector>();
        while (await projector.RunBatchAsync(ct) > 0)
        {
        }
    }

    private sealed class FrozenClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}

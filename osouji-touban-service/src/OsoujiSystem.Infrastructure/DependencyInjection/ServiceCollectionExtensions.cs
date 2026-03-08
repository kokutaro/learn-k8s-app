using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Application.Notifications;
using OsoujiSystem.Application.Queries.Abstractions;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Infrastructure.Cache;
using OsoujiSystem.Infrastructure.Messaging;
using OsoujiSystem.Infrastructure.Migrations;
using OsoujiSystem.Infrastructure.Notifications;
using OsoujiSystem.Infrastructure.Observability;
using OsoujiSystem.Infrastructure.Pii;
using OsoujiSystem.Infrastructure.Options;
using OsoujiSystem.Infrastructure.Outbox;
using OsoujiSystem.Infrastructure.Persistence;
using OsoujiSystem.Infrastructure.Persistence.Postgres;
using OsoujiSystem.Infrastructure.Projection;
using OsoujiSystem.Infrastructure.Queries.Caching;
using OsoujiSystem.Infrastructure.Queries.Postgres;
using OsoujiSystem.Infrastructure.Retention;
using OsoujiSystem.Infrastructure.Serialization;
using StackExchange.Redis;

namespace OsoujiSystem.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    internal const string PostgresConnectionName = "osouji-db";
    internal const string RedisConnectionName = "osouji-redis";
    internal const string RabbitMqConnectionName = "osouji-rabbitmq";

    public static IServiceCollection AddOsoujiInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton(configuration);

        services
            .AddOptions<InfrastructureOptions>()
            .Bind(configuration.GetSection(InfrastructureOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration.GetSection(InfrastructureOptions.SectionName).Get<InfrastructureOptions>()
                      ?? new InfrastructureOptions();

        if (string.Equals(options.PersistenceMode, "EventStore", StringComparison.OrdinalIgnoreCase))
        {
            var postgresConnectionString = ResolvePostgresConnectionString(configuration);
            var redisConnectionString = ResolveRedisConnectionString(configuration);
            _ = ResolveRabbitMqConnectionString(configuration);

            services.AddNpgsqlDataSource(postgresConnectionString);
            services.AddOpenTelemetry()
                .WithTracing(tracerProviderBuilder =>
                {
                    tracerProviderBuilder.AddNpgsql();
                    tracerProviderBuilder.AddSource("Aspire.RabbitMQ.Client");
                });
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
                redisOptions.AbortOnConnectFail = false;
                return ConnectionMultiplexer.Connect(redisOptions);
            });
            services.AddStackExchangeRedisCache(cacheOptions => cacheOptions.Configuration = redisConnectionString);

            services.AddSingleton<ITransactionContextAccessor, AsyncLocalTransactionContextAccessor>();
            services.AddSingleton<IEventWriteContextAccessor, AsyncLocalEventWriteContextAccessor>();
            services.AddSingleton<InfrastructureJsonSerializer>();
            services.AddSingleton<EventStoreDocuments>();
            services.AddSingleton<PostgresReadModelHelpers>();
            services.AddSingleton<ICacheKeyFactory, CacheKeyFactory>();
            services.AddSingleton<IAggregateCache, RedisAggregateCache>();
            services.AddSingleton<IReadModelCache, RedisReadModelCache>();
            services.AddSingleton<IReadModelCacheKeyFactory, ReadModelCacheKeyFactory>();
            services.AddSingleton<ICacheInvalidationTaskRepository, CacheInvalidationTaskRepository>();
            services.AddSingleton<IConsumerProcessedEventRepository, ConsumerProcessedEventRepository>();
            services.AddSingleton<INotificationDeliveryLogRepository, NotificationDeliveryLogRepository>();
            services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
            services.AddScoped<INotificationChannel, EMailNotificationChannel>();
            services.AddSingleton<INotificationRabbitMqMessageHandler, NotificationRabbitMqMessageHandler>();
            services.AddSingleton<IIntegrationRabbitMqMessageHandler, UserRegistryIntegrationRabbitMqMessageHandler>();
            services.AddSingleton<IPiiAnonymizer, HmacPiiAnonymizer>();
            services.AddScoped<IApplicationTransaction, NpgsqlApplicationTransaction>();
            services.AddScoped<IDomainEventDispatcher, OutboxDomainEventDispatcher>();
            services.AddScoped<ICleaningAreaRepository, EventStoreCleaningAreaRepository>();
            services.AddScoped<IFacilityRepository, EventStoreFacilityRepository>();
            services.AddScoped<IWeeklyDutyPlanRepository, EventStoreWeeklyDutyPlanRepository>();
            services.AddScoped<IManagedUserRepository, EventStoreManagedUserRepository>();
            services.AddScoped<IAssignmentHistoryRepository, EventStoreAssignmentHistoryRepository>();
            services.AddScoped<IFacilityDirectoryProjectionRepository, PostgresFacilityDirectoryProjectionRepository>();
            services.AddScoped<IUserDirectoryProjectionRepository, PostgresUserDirectoryProjectionRepository>();
            services.AddScoped<WeeklyPlanNotificationFactory>();
            services.AddScoped<PostgresFacilityReadRepository>();
            services.AddScoped<PostgresCleaningAreaReadRepository>();
            services.AddScoped<PostgresWeeklyDutyPlanReadRepository>();
            services.AddScoped<IFacilityReadRepository, CachedFacilityReadRepository>();
            services.AddScoped<ICleaningAreaReadRepository, CachedCleaningAreaReadRepository>();
            services.AddScoped<IWeeklyDutyPlanReadRepository, CachedWeeklyDutyPlanReadRepository>();
            services.AddSingleton<MainProjector>();
            services.AddHostedService<DevelopmentDbMigrationHostedService>();
            services.AddHostedService<RabbitMqTopologyHostedService>();
            services.AddHostedService<MainProjectionWorker>();
            services.AddHostedService<InfrastructureMetricsCollectorWorker>();
            services.AddHostedService<CacheInvalidationRecoveryWorker>();
            services.AddHostedService<OutboxPublisherWorker>();
            services.AddHostedService<NotificationConsumerWorker>();
            services.AddHostedService<IntegrationConsumerWorker>();
            services.AddHostedService<RetentionPurgeWorker>();
            return services;
        }

        services.AddScoped<IApplicationTransaction, StubApplicationTransaction>();
        services.AddScoped<ICleaningAreaRepository, StubCleaningAreaRepository>();
        services.AddScoped<IFacilityRepository, StubFacilityRepository>();
        services.AddScoped<IWeeklyDutyPlanRepository, StubWeeklyDutyPlanRepository>();
        services.AddScoped<IManagedUserRepository, StubManagedUserRepository>();
        services.AddScoped<IAssignmentHistoryRepository, StubAssignmentHistoryRepository>();
        services.AddScoped<IFacilityDirectoryProjectionRepository, StubFacilityDirectoryProjectionRepository>();
        services.AddScoped<IUserDirectoryProjectionRepository, StubUserDirectoryProjectionRepository>();
        services.AddScoped<IFacilityReadRepository, StubFacilityReadRepository>();
        services.AddScoped<ICleaningAreaReadRepository, StubCleaningAreaReadRepository>();
        services.AddScoped<IWeeklyDutyPlanReadRepository, StubWeeklyDutyPlanReadRepository>();

        return services;
    }

    internal static string ResolvePostgresConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(PostgresConnectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{PostgresConnectionName} is required for EventStore mode.");
        }

        return connectionString;
    }

    internal static string ResolveRedisConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(RedisConnectionName);
        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                $"ConnectionStrings:{RedisConnectionName} is required for EventStore mode.")
            : connectionString;
    }

    internal static string ResolveRabbitMqConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(RabbitMqConnectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{RabbitMqConnectionName} is required for EventStore mode.");
        }

        return connectionString;
    }
}

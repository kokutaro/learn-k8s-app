using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Repositories;
using OsoujiSystem.Infrastructure.Cache;
using OsoujiSystem.Infrastructure.Migrations;
using OsoujiSystem.Infrastructure.Options;
using OsoujiSystem.Infrastructure.Persistence;
using OsoujiSystem.Infrastructure.Persistence.Postgres;
using OsoujiSystem.Infrastructure.Projection;
using StackExchange.Redis;

namespace OsoujiSystem.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOsoujiInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<InfrastructureOptions>()
            .Bind(configuration.GetSection(InfrastructureOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration.GetSection(InfrastructureOptions.SectionName).Get<InfrastructureOptions>()
            ?? new InfrastructureOptions();

        if (string.Equals(options.PersistenceMode, "EventStore", StringComparison.OrdinalIgnoreCase))
        {
            var redisConnectionString = ResolveRedisConnectionString(options.Redis.ConnectionString);

            services.AddSingleton(sp =>
            {
                var infraOptions = sp.GetRequiredService<IOptions<InfrastructureOptions>>().Value;
                var connectionString = ResolveConnectionString(infraOptions.Postgres.ConnectionString);
                return new NpgsqlDataSourceBuilder(connectionString).Build();
            });
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
            services.AddStackExchangeRedisCache(cacheOptions => cacheOptions.Configuration = redisConnectionString);

            services.AddSingleton<ITransactionContextAccessor, AsyncLocalTransactionContextAccessor>();
            services.AddSingleton<ICacheKeyFactory, CacheKeyFactory>();
            services.AddSingleton<IAggregateCache, RedisAggregateCache>();
            services.AddSingleton<ICacheInvalidationTaskRepository, CacheInvalidationTaskRepository>();
            services.AddScoped<IApplicationTransaction, NpgsqlApplicationTransaction>();
            services.AddScoped<ICleaningAreaRepository, EventStoreCleaningAreaRepository>();
            services.AddScoped<IWeeklyDutyPlanRepository, EventStoreWeeklyDutyPlanRepository>();
            services.AddScoped<IAssignmentHistoryRepository, EventStoreAssignmentHistoryRepository>();
            services.AddSingleton<MainProjector>();
            services.AddHostedService<DevelopmentDbMigrationHostedService>();
            services.AddHostedService<MainProjectionWorker>();
            services.AddHostedService<CacheInvalidationRecoveryWorker>();
            return services;
        }

        services.AddScoped<IApplicationTransaction, StubApplicationTransaction>();
        services.AddScoped<ICleaningAreaRepository, StubCleaningAreaRepository>();
        services.AddScoped<IWeeklyDutyPlanRepository, StubWeeklyDutyPlanRepository>();
        services.AddScoped<IAssignmentHistoryRepository, StubAssignmentHistoryRepository>();

        return services;
    }

    internal static string ResolveConnectionString(string? configured)
    {
        var env = Environment.GetEnvironmentVariable("INFRASTRUCTURE__POSTGRES__CONNECTIONSTRING");
        var connectionString = string.IsNullOrWhiteSpace(env) ? configured : env;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Infrastructure:Postgres:ConnectionString is required for EventStore mode.");
        }

        return connectionString;
    }

    internal static string ResolveRedisConnectionString(string? configured)
    {
        var env = Environment.GetEnvironmentVariable("INFRASTRUCTURE__REDIS__CONNECTIONSTRING");
        var connectionString = string.IsNullOrWhiteSpace(env) ? configured : env;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Infrastructure:Redis:ConnectionString is required for EventStore mode.");
        }

        return connectionString;
    }
}

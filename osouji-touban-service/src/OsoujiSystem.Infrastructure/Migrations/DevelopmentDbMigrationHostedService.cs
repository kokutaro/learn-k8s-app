using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OsoujiSystem.Infrastructure.DependencyInjection;
using OsoujiSystem.Infrastructure.Options;

namespace OsoujiSystem.Infrastructure.Migrations;

internal sealed class DevelopmentDbMigrationHostedService(
    IHostEnvironment environment,
    IOptions<InfrastructureOptions> options,
    ILogger<DevelopmentDbMigrationHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return Task.CompletedTask;
        }

        try
        {
            var connectionString = ServiceCollectionExtensions.ResolveConnectionString(options.Value.Postgres.ConnectionString);
            var result = DbMigrator.Migrate(connectionString);
            if (!result.Successful)
            {
                throw result.Error ?? new InvalidOperationException("Infrastructure database migration failed.");
            }

            logger.LogInformation("Infrastructure database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply infrastructure database migrations.");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

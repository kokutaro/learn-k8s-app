using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OsoujiSystem.Infrastructure.DependencyInjection;

namespace OsoujiSystem.Infrastructure.Migrations;

internal sealed class DevelopmentDbMigrationHostedService(
    IConfiguration configuration,
    ILogger<DevelopmentDbMigrationHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connectionString = ServiceCollectionExtensions.ResolvePostgresConnectionString(configuration);
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

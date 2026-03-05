using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OsoujiSystem.Infrastructure.DependencyInjection;
using OsoujiSystem.Infrastructure.Options;

namespace OsoujiSystem.Infrastructure.Migrations;

internal sealed class DevelopmentDbMigrationHostedService : IHostedService
{
    private readonly IHostEnvironment _environment;
    private readonly IOptions<InfrastructureOptions> _options;
    private readonly ILogger<DevelopmentDbMigrationHostedService> _logger;

    public DevelopmentDbMigrationHostedService(
        IHostEnvironment environment,
        IOptions<InfrastructureOptions> options,
        ILogger<DevelopmentDbMigrationHostedService> logger)
    {
        _environment = environment;
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return Task.CompletedTask;
        }

        try
        {
            var connectionString = ServiceCollectionExtensions.ResolveConnectionString(_options.Value.Postgres.ConnectionString);
            var result = DbMigrator.Migrate(connectionString);
            if (!result.Successful)
            {
                throw result.Error ?? new InvalidOperationException("Infrastructure database migration failed.");
            }

            _logger.LogInformation("Infrastructure database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply infrastructure database migrations.");
            throw;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

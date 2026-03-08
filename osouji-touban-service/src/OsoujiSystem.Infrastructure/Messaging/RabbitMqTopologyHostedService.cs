using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OsoujiSystem.Infrastructure.Messaging;

internal sealed class RabbitMqTopologyHostedService(
    IConfiguration configuration,
    ILogger<RabbitMqTopologyHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var factory = RabbitMqConnectionFactoryProvider.Create(configuration);

        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);

        logger.LogInformation("RabbitMQ topology declaration completed.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}

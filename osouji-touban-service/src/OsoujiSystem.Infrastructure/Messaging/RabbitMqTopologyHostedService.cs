using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OsoujiSystem.Infrastructure.Options;
using RabbitMQ.Client;

namespace OsoujiSystem.Infrastructure.Messaging;

internal sealed class RabbitMqTopologyHostedService(
    IOptions<InfrastructureOptions> options,
    ILogger<RabbitMqTopologyHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var rabbitOptions = options.Value.RabbitMq;

        var factory = new ConnectionFactory
        {
            HostName = rabbitOptions.Host!,
            Port = rabbitOptions.Port,
            VirtualHost = rabbitOptions.VirtualHost,
            UserName = rabbitOptions.Username!,
            Password = rabbitOptions.Password!
        };

        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);

        logger.LogInformation("RabbitMQ topology declaration completed.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}

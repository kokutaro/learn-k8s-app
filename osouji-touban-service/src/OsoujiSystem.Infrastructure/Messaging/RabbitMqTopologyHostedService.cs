using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OsoujiSystem.Infrastructure.Options;
using RabbitMQ.Client;

namespace OsoujiSystem.Infrastructure.Messaging;

internal sealed class RabbitMqTopologyHostedService : IHostedService
{
    private readonly IOptions<InfrastructureOptions> _options;
    private readonly ILogger<RabbitMqTopologyHostedService> _logger;

    public RabbitMqTopologyHostedService(
        IOptions<InfrastructureOptions> options,
        ILogger<RabbitMqTopologyHostedService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var rabbitOptions = _options.Value.RabbitMq;

        var factory = new ConnectionFactory
        {
            HostName = rabbitOptions.Host,
            Port = rabbitOptions.Port,
            VirtualHost = rabbitOptions.VirtualHost,
            UserName = rabbitOptions.Username,
            Password = rabbitOptions.Password
        };

        using var connection = await factory.CreateConnectionAsync(cancellationToken);
        using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);

        _logger.LogInformation("RabbitMQ topology declaration completed.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}

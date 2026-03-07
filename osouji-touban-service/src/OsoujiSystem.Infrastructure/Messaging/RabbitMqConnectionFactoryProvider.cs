using Microsoft.Extensions.Configuration;
using OsoujiSystem.Infrastructure.DependencyInjection;
using RabbitMQ.Client;

namespace OsoujiSystem.Infrastructure.Messaging;

internal static class RabbitMqConnectionFactoryProvider
{
    public static ConnectionFactory Create(IConfiguration configuration)
    {
        var connectionString = ServiceCollectionExtensions.ResolveRabbitMqConnectionString(configuration);
        return new ConnectionFactory
        {
            Uri = new Uri(connectionString)
        };
    }
}

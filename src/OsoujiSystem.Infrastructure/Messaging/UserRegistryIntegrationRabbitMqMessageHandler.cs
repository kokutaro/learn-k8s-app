using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Events;
using OsoujiSystem.Domain.ValueObjects;
using OsoujiSystem.Infrastructure.Serialization;

namespace OsoujiSystem.Infrastructure.Messaging;

internal sealed class UserRegistryIntegrationRabbitMqMessageHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<UserRegistryIntegrationRabbitMqMessageHandler> logger,
    InfrastructureJsonSerializer jsonSerializer) : IIntegrationRabbitMqMessageHandler
{
    public async Task HandleAsync(
        string consumerName,
        string routingKey,
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, object?> headers,
        CancellationToken ct)
    {
        if (!string.Equals(routingKey, "user-registry.user-registered", StringComparison.Ordinal)
            && !string.Equals(routingKey, "user-registry.user-updated", StringComparison.Ordinal))
        {
            logger.LogDebug("Skipping unsupported routing key {RoutingKey} for {ConsumerName}", routingKey, consumerName);
            return;
        }

        if (!TryReadAggregateVersion(headers, out var aggregateVersion))
        {
            throw new InvalidOperationException("aggregate_version header is required for user-registry events.");
        }

        if (!RabbitMqConsumerWorkerBase<IIntegrationRabbitMqMessageHandler>.TryReadEventId(headers, out var eventId))
        {
            throw new InvalidOperationException("event_id header is required for user-registry events.");
        }

        var projection = string.Equals(routingKey, "user-registry.user-registered", StringComparison.Ordinal)
            ? BuildProjection(
                jsonSerializer.Deserialize<UserRegistered>(body.Span)
                ?? throw new InvalidOperationException("Failed to deserialize UserRegistered event."),
                aggregateVersion)
            : BuildProjection(
                jsonSerializer.Deserialize<UserUpdated>(body.Span)
                ?? throw new InvalidOperationException("Failed to deserialize UserUpdated event."),
                aggregateVersion);

        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserDirectoryProjectionRepository>();
        await repository.UpsertAsync(projection, aggregateVersion, eventId, ct);
    }

    private static UserDirectoryProjection BuildProjection(UserRegistered ev, long aggregateVersion)
        => new(
            new UserId(ev.UserId),
            EmployeeNumber.Create(ev.EmployeeNumber).Value,
            NormalizeDisplayName(ev.DisplayName),
            ev.LifecycleStatus,
            ev.DepartmentCode,
            aggregateVersion,
            ev.EmailAddress);

    private static UserDirectoryProjection BuildProjection(UserUpdated ev, long aggregateVersion)
        => new(
            new UserId(ev.UserId),
            EmployeeNumber.Create(ev.EmployeeNumber).Value,
            NormalizeDisplayName(ev.DisplayName),
            ev.LifecycleStatus,
            ev.DepartmentCode,
            aggregateVersion,
            ev.EmailAddress);

    private static string NormalizeDisplayName(string? displayName)
        => string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName.Trim();

    internal static bool TryReadAggregateVersion(IReadOnlyDictionary<string, object?> headers, out long aggregateVersion)
    {
        if (!headers.TryGetValue("aggregate_version", out var raw) || raw is null)
        {
            aggregateVersion = 0;
            return false;
        }

        switch (raw)
        {
            case int asInt:
                aggregateVersion = asInt;
                return true;
            case long asLong:
                aggregateVersion = asLong;
                return true;
            case string asString when long.TryParse(asString, out var parsed):
                aggregateVersion = parsed;
                return true;
            default:
                aggregateVersion = 0;
                return false;
        }
    }
}

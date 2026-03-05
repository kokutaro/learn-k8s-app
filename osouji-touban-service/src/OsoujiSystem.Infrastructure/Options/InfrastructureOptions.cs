using System.ComponentModel.DataAnnotations;

namespace OsoujiSystem.Infrastructure.Options;

public sealed class InfrastructureOptions
{
    public const string SectionName = "Infrastructure";

    [Required]
    [RegularExpression("^(Stub|EventStore)$")]
    public string PersistenceMode { get; init; } = "Stub";

    [Required]
    public PostgresOptions Postgres { get; init; } = new();

    [Required]
    public RedisOptions Redis { get; init; } = new();

    [Required]
    public RabbitMqOptions RabbitMq { get; init; } = new();

    [Required]
    public OutboxOptions Outbox { get; init; } = new();

    [Required]
    public ProjectionOptions Projection { get; init; } = new();

    [Required]
    public RetentionOptions Retention { get; init; } = new();

    [Required]
    public PiiOptions Pii { get; init; } = new();
}

public sealed class PostgresOptions
{
    public string? ConnectionString { get; init; }

    [Required]
    public string Schema { get; init; } = "public";

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; init; } = 30;
}

public sealed class RedisOptions
{
    public string? ConnectionString { get; init; }

    [Range(1, 86400)]
    public int DefaultTtlSeconds { get; init; } = 300;
}

public sealed class RabbitMqOptions
{
    public string? Host { get; init; }

    [Range(1, 65535)]
    public int Port { get; init; } = 5672;

    [Required]
    public string VirtualHost { get; init; } = "/";

    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool UseTls { get; init; } = true;
}

public sealed class OutboxOptions
{
    [Range(1, 5000)]
    public int BatchSize { get; init; } = 100;

    [Range(10, 600000)]
    public int PollIntervalMs { get; init; } = 1000;
}

public sealed class ProjectionOptions
{
    [Range(1, 5000)]
    public int BatchSize { get; init; } = 200;

    [Range(10, 600000)]
    public int PollIntervalMs { get; init; } = 1000;
}

public sealed class RetentionOptions
{
    [Required]
    public string DailyRunJst { get; init; } = "03:30";

    [Range(1, 50)]
    public int EventStoreYears { get; init; } = 7;

    [Range(1, 5000)]
    public int OutboxPublishedDays { get; init; } = 180;

    [Range(1, 5000)]
    public int OutboxFailedDays { get; init; } = 365;

    [Range(1, 3650)]
    public int DlqDays { get; init; } = 30;

    [Range(1, 5000)]
    public int LogDays { get; init; } = 180;

    [Range(1, 3650)]
    public int TraceDays { get; init; } = 14;

    [Range(1, 120)]
    public int MetricsMonths { get; init; } = 13;
}

public sealed class PiiOptions
{
    [Required]
    public string TenantSaltSecretName { get; init; } = "osouji-pii-salt";

    public bool MaskEmployeeNumber { get; init; } = true;
}

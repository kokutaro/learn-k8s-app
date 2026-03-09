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
    public ProjectionVisibilityOptions ProjectionVisibility { get; init; } = new();

    [Required]
    public RetentionOptions Retention { get; init; } = new();

    [Required]
    public PiiOptions Pii { get; init; } = new();
}

public sealed class PostgresOptions
{
    [Required]
    public string Schema { get; init; } = "public";

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; init; } = 30;
}

public sealed class RedisOptions
{
    [Range(1, 86400)]
    public int DefaultTtlSeconds { get; init; } = 300;

    [Range(1, 604800)]
    public int ReadModelDetailTtlSeconds { get; init; } = 86400;

    [Range(1, 86400)]
    public int ReadModelListTtlSeconds { get; init; } = 600;

    [Range(1, 300)]
    public int ReadModelNegativeTtlSeconds { get; init; } = 15;

    [Range(1, 1000)]
    public int ReadModelCacheMaxListLimit { get; init; } = 50;

    public bool ReadModelWarmEnabled { get; init; } = true;

    [Range(1, 10)]
    public int ReadModelWarmTopPages { get; init; } = 3;
}

public sealed class RabbitMqOptions
{
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

public sealed class ProjectionVisibilityOptions
{
    public bool Enabled { get; init; } = false;

    [Range(10, 600000)]
    public int WaitTimeoutMs { get; init; } = 3000;

    [Range(10, 600000)]
    public int PollIntervalMs { get; init; } = 50;
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

using Dapper;
using Npgsql;
using OsoujiSystem.Application.Abstractions;
using OsoujiSystem.Domain.Entities.Facilities;
using OsoujiSystem.Infrastructure.Serialization;

namespace OsoujiSystem.Infrastructure.Persistence.Postgres;

internal sealed class PostgresFacilityDirectoryProjectionRepository(
    NpgsqlDataSource dataSource,
    ITransactionContextAccessor transactionContextAccessor,
    IEventWriteContextAccessor eventWriteContextAccessor,
    EventStoreDocuments eventStoreDocuments,
    InfrastructureJsonSerializer jsonSerializer)
    : PostgresRepositoryBase(dataSource, transactionContextAccessor, eventWriteContextAccessor, eventStoreDocuments, jsonSerializer), IFacilityDirectoryProjectionRepository
{
    public Task<FacilityDirectoryProjection?> FindByFacilityIdAsync(FacilityId facilityId, CancellationToken ct)
        => ExecuteReadAsync<FacilityDirectoryProjection?>(async (connection, transaction) =>
        {
            var row = await connection.QuerySingleOrDefaultAsync<Row>(
                """
                SELECT facility_id AS FacilityId,
                       facility_code AS FacilityCode,
                       name AS Name,
                       description AS Description,
                       time_zone_id AS TimeZoneId,
                       lifecycle_status AS LifecycleStatus,
                       aggregate_version AS AggregateVersion
                FROM projection_facilities
                WHERE facility_id = @facilityId;
                """,
                new { facilityId = facilityId.Value },
                transaction: transaction);

            return row is null
                ? null
                : new FacilityDirectoryProjection(
                    new FacilityId(row.FacilityId),
                    FacilityCode.Create(row.FacilityCode).Value,
                    row.Name,
                    row.Description,
                    FacilityTimeZone.Create(row.TimeZoneId).Value,
                    Enum.Parse<FacilityLifecycleStatus>(row.LifecycleStatus, ignoreCase: true),
                    row.AggregateVersion);
        }, ct);

    public Task UpsertAsync(FacilityDirectoryProjection projection, long aggregateVersion, Guid sourceEventId, CancellationToken ct)
        => ExecuteWriteAsync(async (connection, transaction) =>
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO projection_facilities (
                    facility_id,
                    facility_code,
                    name,
                    description,
                    time_zone_id,
                    lifecycle_status,
                    source_event_id,
                    aggregate_version,
                    updated_at
                )
                VALUES (
                    @facilityId,
                    @facilityCode,
                    @name,
                    @description,
                    @timeZoneId,
                    @lifecycleStatus,
                    @sourceEventId,
                    @aggregateVersion,
                    now()
                )
                ON CONFLICT (facility_id)
                DO UPDATE SET
                    facility_code = EXCLUDED.facility_code,
                    name = EXCLUDED.name,
                    description = EXCLUDED.description,
                    time_zone_id = EXCLUDED.time_zone_id,
                    lifecycle_status = EXCLUDED.lifecycle_status,
                    source_event_id = EXCLUDED.source_event_id,
                    aggregate_version = EXCLUDED.aggregate_version,
                    updated_at = now()
                WHERE projection_facilities.aggregate_version <= EXCLUDED.aggregate_version;
                """,
                new
                {
                    facilityId = projection.FacilityId.Value,
                    facilityCode = projection.FacilityCode.Value,
                    name = projection.Name,
                    description = projection.Description,
                    timeZoneId = projection.TimeZone.Value,
                    lifecycleStatus = projection.LifecycleStatus.ToString(),
                    sourceEventId,
                    aggregateVersion
                },
                transaction: transaction);
        }, ct);

    private sealed record Row(
        Guid FacilityId,
        string FacilityCode,
        string Name,
        string? Description,
        string TimeZoneId,
        string LifecycleStatus,
        long AggregateVersion);
}

using OsoujiSystem.Domain.Entities.Facilities;

namespace OsoujiSystem.Application.Abstractions;

public sealed record FacilityDirectoryProjection(
    FacilityId FacilityId,
    FacilityCode FacilityCode,
    string Name,
    string? Description,
    FacilityTimeZone TimeZone,
    FacilityLifecycleStatus LifecycleStatus,
    long AggregateVersion);

public interface IFacilityDirectoryProjectionRepository
{
    Task<FacilityDirectoryProjection?> FindByFacilityIdAsync(
        FacilityId facilityId,
        CancellationToken ct);

    Task UpsertAsync(
        FacilityDirectoryProjection projection,
        long aggregateVersion,
        Guid sourceEventId,
        CancellationToken ct);
}

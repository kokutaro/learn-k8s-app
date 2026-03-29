namespace OsoujiSystem.Infrastructure.Queries.Caching;

internal sealed class ReadModelCacheKeyFactory : IReadModelCacheKeyFactory
{
    public string FacilityDetailVersion(Guid facilityId, long version)
        => $"readmodel:facility:{facilityId:D}:v{version}";

    public string FacilityDetailLatest(Guid facilityId)
        => $"readmodel:facility:{facilityId:D}:latest";

    public string FacilityMissing(Guid facilityId)
        => $"readmodel:facility:{facilityId:D}:missing";

    public string FacilitiesListNamespace()
        => "readmodel:ns:facilities:list";

    public string FacilitiesListResult(long namespaceVersion, string queryHash)
        => $"readmodel:list:facilities:n{namespaceVersion}:q{queryHash}";

    public string CleaningAreaDetailVersion(Guid areaId, long version)
        => $"readmodel:cleaning-area:{areaId:D}:v{version}";

    public string CleaningAreaDetailLatest(Guid areaId)
        => $"readmodel:cleaning-area:{areaId:D}:latest";

    public string CleaningAreaMissing(Guid areaId)
        => $"readmodel:cleaning-area:{areaId:D}:missing";

    public string CleaningAreasListNamespace()
        => "readmodel:ns:cleaning-areas:list";

    public string CleaningAreasListResult(long namespaceVersion, string queryHash)
        => $"readmodel:list:cleaning-areas:n{namespaceVersion}:q{queryHash}";

    public string WeeklyDutyPlanDetailVersion(Guid planId, long version)
        => $"readmodel:weekly-plan:{planId:D}:v{version}";

    public string WeeklyDutyPlanDetailLatest(Guid planId)
        => $"readmodel:weekly-plan:{planId:D}:latest";

    public string WeeklyDutyPlanMissing(Guid planId)
        => $"readmodel:weekly-plan:{planId:D}:missing";

    public string WeeklyDutyPlansListNamespace()
        => "readmodel:ns:weekly-duty-plans:list";

    public string WeeklyDutyPlansListResult(long namespaceVersion, string queryHash)
        => $"readmodel:list:weekly-duty-plans:n{namespaceVersion}:q{queryHash}";
}

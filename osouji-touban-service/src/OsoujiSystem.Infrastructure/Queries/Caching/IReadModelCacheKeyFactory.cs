namespace OsoujiSystem.Infrastructure.Queries.Caching;

internal interface IReadModelCacheKeyFactory
{
    string CleaningAreaDetailVersion(Guid areaId, long version);
    string CleaningAreaDetailLatest(Guid areaId);
    string CleaningAreaMissing(Guid areaId);
    string CleaningAreasListNamespace();
    string CleaningAreasListResult(long namespaceVersion, string queryHash);
    string WeeklyDutyPlanDetailVersion(Guid planId, long version);
    string WeeklyDutyPlanDetailLatest(Guid planId);
    string WeeklyDutyPlanMissing(Guid planId);
    string WeeklyDutyPlansListNamespace();
    string WeeklyDutyPlansListResult(long namespaceVersion, string queryHash);
}

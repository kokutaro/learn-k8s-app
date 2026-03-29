using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Infrastructure.Cache;

internal sealed class CacheKeyFactory : ICacheKeyFactory
{
    public string WeeklyPlanVersion(WeeklyDutyPlanId planId, long version)
        => $"weekly-plan:{planId.Value:D}:v{version}";

    public string WeeklyPlanLatest(WeeklyDutyPlanId planId)
        => $"weekly-plan:{planId.Value:D}:latest";

    public string WeeklyPlanAreaWeekLatest(CleaningAreaId areaId, WeekId weekId)
        => $"weekly-plan:{areaId.Value:D}:{weekId.Year:0000}-{weekId.WeekNumber:00}:latest";

    public string CleaningAreaVersion(CleaningAreaId areaId, long version)
        => $"cleaning-area:{areaId.Value:D}:v{version}";

    public string CleaningAreaLatest(CleaningAreaId areaId)
        => $"cleaning-area:{areaId.Value:D}:latest";

    public string CleaningAreaUserLatest(UserId userId)
        => $"cleaning-area:user:{userId.Value:D}:latest";
}

using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Infrastructure.Cache;

internal interface ICacheKeyFactory
{
    string WeeklyPlanVersion(WeeklyDutyPlanId planId, long version);
    string WeeklyPlanLatest(WeeklyDutyPlanId planId);
    string WeeklyPlanAreaWeekLatest(CleaningAreaId areaId, WeekId weekId);

    string CleaningAreaVersion(CleaningAreaId areaId, long version);
    string CleaningAreaLatest(CleaningAreaId areaId);
    string CleaningAreaUserLatest(UserId userId);
}

using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.Facilities;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;

namespace OsoujiSystem.Application.Abstractions;

public interface IIdGenerator
{
    UserId NewUserId();
    FacilityId NewFacilityId();
    WeeklyDutyPlanId NewWeeklyDutyPlanId();
    AreaMemberId NewAreaMemberId();
}

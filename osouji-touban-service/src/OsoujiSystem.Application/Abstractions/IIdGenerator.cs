using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;

namespace OsoujiSystem.Application.Abstractions;

public interface IIdGenerator
{
    UserId NewUserId();
    WeeklyDutyPlanId NewWeeklyDutyPlanId();
    AreaMemberId NewAreaMemberId();
}

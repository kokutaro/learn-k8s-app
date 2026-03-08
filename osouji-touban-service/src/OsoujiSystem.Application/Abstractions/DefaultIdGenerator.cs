using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;

namespace OsoujiSystem.Application.Abstractions;

public sealed class DefaultIdGenerator : IIdGenerator
{
    public UserId NewUserId() => UserId.New();

    public WeeklyDutyPlanId NewWeeklyDutyPlanId() => WeeklyDutyPlanId.New();

    public AreaMemberId NewAreaMemberId() => AreaMemberId.New();
}

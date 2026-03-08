using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;

namespace OsoujiSystem.Domain.Errors;

public abstract record WeeklyDutyPlanError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?> Args) : DomainError(Code, Message, Args);

public sealed record WeekAlreadyClosedError(WeeklyDutyPlanId PlanId) : WeeklyDutyPlanError(
    nameof(WeekAlreadyClosedError),
    "クローズ済みの週次計画は再計算できません。",
    new Dictionary<string, object?> { ["planId"] = PlanId.ToString() });

public sealed record AssignmentConflictError(CleaningSpotId SpotId) : WeeklyDutyPlanError(
    nameof(AssignmentConflictError),
    "同一掃除箇所に複数担当が割り当てられています。",
    new Dictionary<string, object?> { ["spotId"] = SpotId.ToString() });

public sealed record NoAvailableUserForSpotError(CleaningSpotId SpotId) : WeeklyDutyPlanError(
    nameof(NoAvailableUserForSpotError),
    "割り当て可能なユーザーが存在しません。",
    new Dictionary<string, object?> { ["spotId"] = SpotId.ToString() });

public sealed record InvalidRebalanceRequestError(string Reason) : WeeklyDutyPlanError(
    nameof(InvalidRebalanceRequestError),
    "再配分リクエストが不正です。",
    new Dictionary<string, object?> { ["reason"] = Reason });

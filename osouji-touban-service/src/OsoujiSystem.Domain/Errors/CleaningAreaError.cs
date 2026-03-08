using OsoujiSystem.Domain.Entities.CleaningAreas;

namespace OsoujiSystem.Domain.Errors;

public abstract record CleaningAreaError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?> Args) : DomainError(Code, Message, Args);

public sealed record CleaningAreaHasNoSpotError(CleaningAreaId AreaId) : CleaningAreaError(
    nameof(CleaningAreaHasNoSpotError),
    "掃除箇所は1件以上必要です。",
    new Dictionary<string, object?> { ["areaId"] = AreaId.ToString() });

public sealed record DuplicateCleaningSpotError(CleaningAreaId AreaId, CleaningSpotId SpotId) : CleaningAreaError(
    nameof(DuplicateCleaningSpotError),
    "同じ掃除箇所は重複登録できません。",
    new Dictionary<string, object?> { ["areaId"] = AreaId.ToString(), ["spotId"] = SpotId.ToString() });

public sealed record DuplicateAreaMemberError(CleaningAreaId AreaId, UserId UserId) : CleaningAreaError(
    nameof(DuplicateAreaMemberError),
    "同じユーザーは同一エリアに重複所属できません。",
    new Dictionary<string, object?> { ["areaId"] = AreaId.ToString(), ["userId"] = UserId.ToString() });

public sealed record UserAlreadyAssignedToAnotherAreaError(UserId UserId, CleaningAreaId CurrentAreaId) : CleaningAreaError(
    nameof(UserAlreadyAssignedToAnotherAreaError),
    "このユーザーはすでに別エリアへ所属しています。",
    new Dictionary<string, object?> { ["userId"] = UserId.ToString(), ["currentAreaId"] = CurrentAreaId.ToString() });

public sealed record InvalidWeekRuleError(string Reason) : CleaningAreaError(
    nameof(InvalidWeekRuleError),
    "週ルールが不正です。",
    new Dictionary<string, object?> { ["reason"] = Reason });

using OsoujiSystem.Domain.Entities.Facilities;

namespace OsoujiSystem.Domain.Errors;

public abstract record FacilityError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?> Args) : DomainError(Code, Message, Args);

public sealed record DuplicateFacilityCodeError(string FacilityCode) : FacilityError(
    nameof(DuplicateFacilityCodeError),
    "同じ施設コードの Facility は重複登録できません。",
    new Dictionary<string, object?> { ["facilityCode"] = FacilityCode });

public sealed record InvalidFacilityCodeError(string Value) : FacilityError(
    nameof(InvalidFacilityCodeError),
    "施設コードが不正です。",
    new Dictionary<string, object?> { ["value"] = Value });

public sealed record InvalidFacilityNameError(string Reason) : FacilityError(
    nameof(InvalidFacilityNameError),
    "施設名が不正です。",
    new Dictionary<string, object?> { ["reason"] = Reason });

public sealed record InvalidFacilityDescriptionError(string Reason) : FacilityError(
    nameof(InvalidFacilityDescriptionError),
    "施設説明が不正です。",
    new Dictionary<string, object?> { ["reason"] = Reason });

public sealed record InvalidFacilityTimeZoneError(string Value) : FacilityError(
    nameof(InvalidFacilityTimeZoneError),
    "施設タイムゾーンが不正です。",
    new Dictionary<string, object?> { ["value"] = Value });

public sealed record FacilityNotActiveError(FacilityId FacilityId, FacilityLifecycleStatus LifecycleStatus) : FacilityError(
    nameof(FacilityNotActiveError),
    "この Facility は現在利用できません。",
    new Dictionary<string, object?>
    {
        ["facilityId"] = FacilityId.ToString(),
        ["lifecycleStatus"] = LifecycleStatus.ToString()
    });

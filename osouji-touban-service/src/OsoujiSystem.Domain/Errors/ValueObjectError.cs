namespace OsoujiSystem.Domain.Errors;

public abstract record ValueObjectError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?> Args) : DomainError(Code, Message, Args);

public sealed record InvalidEmployeeNumberError(string Value) : ValueObjectError(
    nameof(InvalidEmployeeNumberError),
    "社員番号の形式が不正です。",
    new Dictionary<string, object?> { ["value"] = Value });

public sealed record InvalidWeekIdError(int Year, int WeekNumber) : ValueObjectError(
    nameof(InvalidWeekIdError),
    "週識別子が不正です。",
    new Dictionary<string, object?> { ["year"] = Year, ["weekNumber"] = WeekNumber });

public sealed record InvalidWeekRuleTimeZoneError(string TimeZoneId) : ValueObjectError(
    nameof(InvalidWeekRuleTimeZoneError),
    "タイムゾーンIDが不正です。",
    new Dictionary<string, object?> { ["timeZoneId"] = TimeZoneId });

public sealed record InvalidRotationCursorError(int Value) : ValueObjectError(
    nameof(InvalidRotationCursorError),
    "ローテーションカーソルは0以上である必要があります。",
    new Dictionary<string, object?> { ["value"] = Value });

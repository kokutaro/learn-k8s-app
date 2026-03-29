using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.UserManagement;

namespace OsoujiSystem.Domain.Errors;

public abstract record ManagedUserError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?> Args) : DomainError(Code, Message, Args);

public sealed record DuplicateEmployeeNumberError(string EmployeeNumber) : ManagedUserError(
    nameof(DuplicateEmployeeNumberError),
    "同じ社員番号のユーザーは重複登録できません。",
    new Dictionary<string, object?> { ["employeeNumber"] = EmployeeNumber });

public sealed record DuplicateAuthIdentityLinkError(string IdentityProviderKey, string IdentitySubject) : ManagedUserError(
    nameof(DuplicateAuthIdentityLinkError),
    "この認証主体はすでに別ユーザーへ紐付いています。",
    new Dictionary<string, object?>
    {
        ["identityProviderKey"] = IdentityProviderKey,
        ["identitySubject"] = IdentitySubject
    });

public sealed record ManagedUserAlreadyArchivedError(UserId UserId) : ManagedUserError(
    nameof(ManagedUserAlreadyArchivedError),
    "アーカイブ済みユーザーは更新できません。",
    new Dictionary<string, object?> { ["userId"] = UserId.ToString() });

public sealed record ManagedUserNotActiveError(UserId UserId, ManagedUserLifecycleStatus LifecycleStatus) : ManagedUserError(
    nameof(ManagedUserNotActiveError),
    "このユーザーは現在利用できません。",
    new Dictionary<string, object?>
    {
        ["userId"] = UserId.ToString(),
        ["lifecycleStatus"] = LifecycleStatus.ToString()
    });

public sealed record InvalidDisplayNameError(string Reason) : ManagedUserError(
    nameof(InvalidDisplayNameError),
    "表示名が不正です。",
    new Dictionary<string, object?> { ["reason"] = Reason });

public sealed record InvalidEmailAddressError(string Value) : ManagedUserError(
    nameof(InvalidEmailAddressError),
    "メールアドレスが不正です。",
    new Dictionary<string, object?> { ["value"] = Value });

public sealed record InvalidDepartmentCodeError(string Value) : ManagedUserError(
    nameof(InvalidDepartmentCodeError),
    "部門コードが不正です。",
    new Dictionary<string, object?> { ["value"] = Value });

public sealed record InvalidIdentityProviderKeyError(string Value) : ManagedUserError(
    nameof(InvalidIdentityProviderKeyError),
    "Identity provider key が不正です。",
    new Dictionary<string, object?> { ["value"] = Value });

public sealed record InvalidIdentitySubjectError(string Value) : ManagedUserError(
    nameof(InvalidIdentitySubjectError),
    "Identity subject が不正です。",
    new Dictionary<string, object?> { ["value"] = Value });

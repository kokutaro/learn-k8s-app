using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Errors;

namespace OsoujiSystem.Domain.Entities.UserManagement.ValueObjects;

public readonly record struct IdentitySubject(string Value) : IStronglyTypedId<string>
{
    public static Result<IdentitySubject, DomainError> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<IdentitySubject, DomainError>.Failure(new InvalidIdentitySubjectError(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > 200)
        {
            return Result<IdentitySubject, DomainError>.Failure(new InvalidIdentitySubjectError(value));
        }

        return Result<IdentitySubject, DomainError>.Success(new IdentitySubject(normalized));
    }

    public override string ToString() => Value;

    public static implicit operator string(IdentitySubject identitySubject) => identitySubject.Value;
}

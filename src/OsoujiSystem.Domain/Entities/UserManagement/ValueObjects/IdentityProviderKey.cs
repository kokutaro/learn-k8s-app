using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Errors;

namespace OsoujiSystem.Domain.Entities.UserManagement.ValueObjects;

public readonly record struct IdentityProviderKey(string Value) : IStronglyTypedId<string>
{
    public static Result<IdentityProviderKey, DomainError> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<IdentityProviderKey, DomainError>.Failure(new InvalidIdentityProviderKeyError(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > 100)
        {
            return Result<IdentityProviderKey, DomainError>.Failure(new InvalidIdentityProviderKeyError(value));
        }

        return Result<IdentityProviderKey, DomainError>.Success(new IdentityProviderKey(normalized));
    }

    public override string ToString() => Value;

    public static implicit operator string(IdentityProviderKey identityProviderKey) => identityProviderKey.Value;
}

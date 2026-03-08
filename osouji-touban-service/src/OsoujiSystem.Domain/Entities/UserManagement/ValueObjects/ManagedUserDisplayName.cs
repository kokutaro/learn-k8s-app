using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Errors;

namespace OsoujiSystem.Domain.Entities.UserManagement.ValueObjects;

public readonly record struct ManagedUserDisplayName(string Value) : IStronglyTypedId<string>
{
    public static Result<ManagedUserDisplayName, DomainError> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<ManagedUserDisplayName, DomainError>.Failure(new InvalidDisplayNameError("DisplayName is required."));
        }

        var normalized = value.Trim();
        if (normalized.Length > 100)
        {
            return Result<ManagedUserDisplayName, DomainError>.Failure(new InvalidDisplayNameError("DisplayName must be 100 characters or fewer."));
        }

        return Result<ManagedUserDisplayName, DomainError>.Success(new ManagedUserDisplayName(normalized));
    }

    public override string ToString() => Value;

    public static implicit operator string(ManagedUserDisplayName displayName) => displayName.Value;
}

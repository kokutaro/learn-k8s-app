using System.Net.Mail;
using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Errors;

namespace OsoujiSystem.Domain.Entities.UserManagement.ValueObjects;

public readonly record struct ManagedUserEmailAddress(string Value) : IStronglyTypedId<string>
{
    public static Result<ManagedUserEmailAddress, DomainError> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<ManagedUserEmailAddress, DomainError>.Failure(new InvalidEmailAddressError(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > 254)
        {
            return Result<ManagedUserEmailAddress, DomainError>.Failure(new InvalidEmailAddressError(value));
        }

        try
        {
            var mailAddress = new MailAddress(normalized);
            if (!string.Equals(mailAddress.Address, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return Result<ManagedUserEmailAddress, DomainError>.Failure(new InvalidEmailAddressError(value));
            }
        }
        catch
        {
            return Result<ManagedUserEmailAddress, DomainError>.Failure(new InvalidEmailAddressError(value));
        }

        return Result<ManagedUserEmailAddress, DomainError>.Success(new ManagedUserEmailAddress(normalized));
    }

    public override string ToString() => Value;

    public static implicit operator string(ManagedUserEmailAddress emailAddress) => emailAddress.Value;
}

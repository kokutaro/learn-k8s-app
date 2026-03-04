using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Errors;
using System.Text.RegularExpressions;

namespace OsoujiSystem.Domain.ValueObjects;

public readonly record struct EmployeeNumber(string Value) : IStronglyTypedId<string>, IComparable<EmployeeNumber>
{
    public static Result<EmployeeNumber, DomainError> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<EmployeeNumber, DomainError>.Failure(new InvalidEmployeeNumberError(value));
        }

        var normalized = value.Trim();
        if (!Regex.IsMatch(normalized, @"^\d{6}$"))
        {
            return Result<EmployeeNumber, DomainError>.Failure(new InvalidEmployeeNumberError(value));
        }

        return Result<EmployeeNumber, DomainError>.Success(new EmployeeNumber(normalized));
    }

    public int CompareTo(EmployeeNumber other) => string.Compare(Value, other.Value, StringComparison.Ordinal);

    public override string ToString() => Value;

    public static implicit operator string(EmployeeNumber employeeNumber) => employeeNumber.Value;
}

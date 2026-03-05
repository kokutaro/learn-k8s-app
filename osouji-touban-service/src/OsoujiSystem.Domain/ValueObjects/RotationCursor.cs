using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Errors;

namespace OsoujiSystem.Domain.ValueObjects;

public readonly record struct RotationCursor(int Value)
{
    public static RotationCursor Start => new(0);

    public static Result<RotationCursor, DomainError> Create(int value)
    {
        return value < 0
            ? Result<RotationCursor, DomainError>.Failure(new InvalidRotationCursorError(value))
            : Result<RotationCursor, DomainError>.Success(new RotationCursor(value));
    }

    public RotationCursor MoveNext(int modulo)
    {
        return modulo <= 0 ? this : new RotationCursor((Value + 1) % modulo);
    }

    public override string ToString() => Value.ToString();
}
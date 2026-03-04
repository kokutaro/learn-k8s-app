using OsoujiSystem.Domain.Abstractions;
using OsoujiSystem.Domain.Errors;

namespace OsoujiSystem.Domain.ValueObjects;

public readonly record struct RotationCursor(int Value)
{
    public static RotationCursor Start => new(0);

    public static Result<RotationCursor, DomainError> Create(int value)
    {
        if (value < 0)
        {
            return Result<RotationCursor, DomainError>.Failure(new InvalidRotationCursorError(value));
        }

        return Result<RotationCursor, DomainError>.Success(new RotationCursor(value));
    }

    public RotationCursor MoveNext(int modulo)
    {
        if (modulo <= 0)
        {
            return this;
        }

        return new RotationCursor((Value + 1) % modulo);
    }

    public override string ToString() => Value.ToString();
}

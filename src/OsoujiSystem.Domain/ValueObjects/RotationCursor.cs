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
        return modulo <= 0 ? this : new RotationCursor((Normalize(modulo) + 1) % modulo);
    }

    public int Normalize(int modulo)
    {
        return modulo <= 0
            ? 0
            : ((Value % modulo) + modulo) % modulo;
    }

    public int CircularDistanceTo(RotationCursor other, int modulo)
    {
        if (modulo <= 0)
        {
            return 0;
        }

        var from = Normalize(modulo);
        var to = other.Normalize(modulo);
        var forward = (to - from + modulo) % modulo;
        var backward = (from - to + modulo) % modulo;
        return Math.Min(forward, backward);
    }

    public override string ToString() => Value.ToString();
}
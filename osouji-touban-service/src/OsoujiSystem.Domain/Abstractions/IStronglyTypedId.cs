namespace OsoujiSystem.Domain.Abstractions;

public interface IStronglyTypedId<out TValue>
where TValue : IEquatable<TValue>
{
    TValue Value { get; }
}

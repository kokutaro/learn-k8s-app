namespace OsoujiSystem.Domain.Abstractions;

public interface IStronglyTypedId<TValue>
{
    TValue Value { get; }
}

namespace OsoujiSystem.Domain.ValueObjects;

public readonly record struct PlanRevision(int Value)
{
    public static PlanRevision Initial => new(1);

    public PlanRevision Next() => new(Value + 1);

    public override string ToString() => Value.ToString();
}

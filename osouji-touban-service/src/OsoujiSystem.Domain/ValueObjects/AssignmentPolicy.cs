namespace OsoujiSystem.Domain.ValueObjects;

public readonly record struct AssignmentPolicy(int FairnessWindowWeeks)
{
    public static AssignmentPolicy Default => new(4);
}

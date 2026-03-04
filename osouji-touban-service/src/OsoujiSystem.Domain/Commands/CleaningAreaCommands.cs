using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Commands;

public sealed record RegisterCleaningAreaCommand
{
    public required CleaningAreaId AreaId { get; init; }
    public required string Name { get; init; }
    public required WeekRule InitialWeekRule { get; init; }
}

public sealed record ScheduleWeekRuleChangeCommand
{
    public required CleaningAreaId AreaId { get; init; }
    public required WeekRule NextWeekRule { get; init; }
}

public sealed record AddCleaningSpotCommand
{
    public required CleaningAreaId AreaId { get; init; }
    public required CleaningSpotId SpotId { get; init; }
    public required string SpotName { get; init; }
    public required int SortOrder { get; init; }
}

public sealed record RemoveCleaningSpotCommand
{
    public required CleaningAreaId AreaId { get; init; }
    public required CleaningSpotId SpotId { get; init; }
}

public sealed record AssignUserToAreaCommand
{
    public required CleaningAreaId AreaId { get; init; }
    public required AreaMemberId AreaMemberId { get; init; }
    public required UserId UserId { get; init; }
    public required EmployeeNumber EmployeeNumber { get; init; }
}

public sealed record UnassignUserFromAreaCommand
{
    public required CleaningAreaId AreaId { get; init; }
    public required UserId UserId { get; init; }
}

public sealed record TransferUserToAreaCommand
{
    public required CleaningAreaId FromAreaId { get; init; }
    public required CleaningAreaId ToAreaId { get; init; }
    public required UserId UserId { get; init; }
}

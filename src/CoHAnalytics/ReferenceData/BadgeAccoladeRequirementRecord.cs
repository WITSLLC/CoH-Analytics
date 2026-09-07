namespace CoHAnalytics.ReferenceData;

/// <summary>Directed prerequisite edge between accolade badges.</summary>
public sealed record BadgeAccoladeRequirementRecord
{
    public required string AccoladeBadgeId { get; init; }

    public required string PrerequisiteBadgeId { get; init; }

    public required int PrerequisiteIndex { get; init; }

    public int LogicGroup { get; init; }

    public required ReferenceRequirementLogicStatus RequirementLogicStatus { get; init; }
}

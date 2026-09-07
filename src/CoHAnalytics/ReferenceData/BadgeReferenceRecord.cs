namespace CoHAnalytics.ReferenceData;

/// <summary>Canonical badge detail record in the item reference catalog.</summary>
public sealed record BadgeReferenceRecord
{
    public required string CatalogItemId { get; init; }

    public required string HomecomingSourceId { get; init; }

    public uint? SetTitleId { get; init; }

    public required string CanonicalCategory { get; init; }

    public required uint BadgeType { get; init; }

    public required ReferenceBadgeKind ReferenceKind { get; init; }

    public required string HeroName { get; init; }

    public required string VillainName { get; init; }

    public string? HeroDescription { get; init; }

    public string? VillainDescription { get; init; }

    public string? HeroIcon { get; init; }

    public string? VillainIcon { get; init; }

    public string? ZoneId { get; init; }

    public string? CompletionBadgeId { get; init; }

    public bool IsZoneCompletionBadge { get; init; }

    public required ReferenceVerificationStatus VerificationStatus { get; init; }

    public string? RequirementText { get; init; }

    public string? RewardText { get; init; }

    public ReferenceRequirementLogicStatus? RequirementLogicStatus { get; init; }

    public ReferenceRequirementLogicPattern? RequirementLogicPattern { get; init; }
}

namespace CoHAnalytics.ReferenceData;

/// <summary>Result of resolving observed item text against the internal reference catalog.</summary>
public sealed record ItemReferenceResolution
{
    public required ItemReferenceRecord Item { get; init; }

    public required string CatalogVersion { get; init; }

    public required string MatchedAliasText { get; init; }
}

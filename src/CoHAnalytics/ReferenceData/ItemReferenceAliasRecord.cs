namespace CoHAnalytics.ReferenceData;

/// <summary>First-class alias spelling linked to a catalog item identity.</summary>
public sealed record ItemReferenceAliasRecord
{
    public required string CatalogItemId { get; init; }

    public required string Locale { get; init; }

    public required string Text { get; init; }

    public required ReferenceAliasNameKind NameKind { get; init; }

    public required bool IsPreferred { get; init; }
}

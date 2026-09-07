namespace CoHAnalytics.ReferenceData;

/// <summary>Minimum common item record in the internal reference catalog.</summary>
public sealed record ItemReferenceRecord
{
    public required string CatalogItemId { get; init; }

    public required ReferenceItemFamily Family { get; init; }

    public required string Subtype { get; init; }

    /// <summary>
    /// Canonical Homecoming Enhancement family for non-set logical Enhancements.
    /// Null for set pieces and non-enhancement items.
    /// </summary>
    public ReferenceEnhancementFamily? EnhancementFamily { get; init; }

    public required string CurrentDisplayName { get; init; }

    public required ReferenceActiveStatus ActiveStatus { get; init; }

    /// <summary>
    /// Server-specific content availability. Required for Enhancements; absent for other families.
    /// Separate from <see cref="ActiveStatus"/>, which tracks authored catalog lifecycle.
    /// </summary>
    public IReadOnlyList<ReferenceServerAvailability> ServerAvailability { get; init; } =
        Array.Empty<ReferenceServerAvailability>();

    public required ReferenceVerificationStatus VerificationStatus { get; init; }

    /// <summary>Salvage rarity when verified (family-specific enrichment).</summary>
    public string? Rarity { get; init; }

    /// <summary>Salvage origin when verified (Tech / Arcane).</summary>
    public string? Origin { get; init; }

    /// <summary>Salvage tier when verified (Low / Mid / High).</summary>
    public string? Tier { get; init; }

    /// <summary>Enhancement set identity when this item is a set piece.</summary>
    public string? EnhancementSetId { get; init; }

    /// <summary>Canonical Enhancement identity produced by a Recipe.</summary>
    public string? ProducedItemId { get; init; }

    /// <summary>Enhancement variant when verified (Regular / Superior / Attuned).</summary>
    public string? Variant { get; init; }

    /// <summary>
    /// Canonical Common IO classification from Homecoming non-origin
    /// <c>boosts_allowed</c> BOOST_TYPE (e.g. Recharge, EnduranceDiscount).
    /// Null for set pieces and non-enhancements.
    /// </summary>
    public string? CommonIoBoostType { get; init; }

    /// <summary>
    /// Analytics UI presentation label mapped from <see cref="CommonIoBoostType"/>.
    /// Presentation metadata only — never replaces the structural key.
    /// </summary>
    public string? CommonIoBoostTypeDisplayText { get; init; }

    /// <summary>
    /// Preferred Enhancement artwork identity (.tga filename) when all source
    /// variants agree. When variants differ, this is null and per-variant icons
    /// are retained on <see cref="SourceVariants"/>.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>Resolved Homecoming display_help for the logical Enhancement.</summary>
    public string? DisplayHelp { get; init; }

    /// <summary>Resolved Homecoming short_help when useful and available.</summary>
    public string? ShortHelp { get; init; }

    /// <summary>Canonical Homecoming powers.bin source identity for Inspirations.</summary>
    public string? HomecomingSourceId { get; init; }

    /// <summary>Homecoming category segment from the Inspiration source identity.</summary>
    public string? HomecomingCategory { get; init; }

    /// <summary>Standard Inspiration tier when applicable (Small/Medium/Large/Super).</summary>
    public string? InspirationStandardTier { get; init; }

    /// <summary>Deterministic Inspiration form metadata from Homecoming discovery.</summary>
    public string? InspirationForm { get; init; }

    /// <summary>Homecoming display-name message key when available.</summary>
    public string? DisplayNameMessageKey { get; init; }

    /// <summary>Homecoming display-help message key when available.</summary>
    public string? DisplayHelpMessageKey { get; init; }

    /// <summary>Homecoming short-help message key when available.</summary>
    public string? ShortHelpMessageKey { get; init; }

    /// <summary>
    /// Concrete Homecoming boost variants that share this logical Enhancement
    /// (Crafted / Attuned / Superior_Attuned, …). Empty for non-enhancements.
    /// </summary>
    public IReadOnlyList<EnhancementSourceVariantReferenceRecord> SourceVariants { get; init; } =
        Array.Empty<EnhancementSourceVariantReferenceRecord>();

    /// <summary>
    /// Per-level Homecoming crafting facts for a logical Recipe. Empty for non-recipes
    /// and for exception-path Recipe identities that have not been promoted.
    /// Reference ValidLevels never include values above 50.
    /// </summary>
    public IReadOnlyList<RecipeLevelReferenceRecord> RecipeLevels { get; init; } =
        Array.Empty<RecipeLevelReferenceRecord>();

    /// <summary>
    /// Homecoming source rows above the Reference Recipe cap (51–53) that share this
    /// logical Recipe identity. Not selectable Recipe levels.
    /// </summary>
    public IReadOnlyList<RecipeExcludedSourceLevelReferenceRecord> ExcludedHomecomingSourceLevels { get; init; } =
        Array.Empty<RecipeExcludedSourceLevelReferenceRecord>();

    /// <summary>Distinct promoted Recipe levels, always ≤ 50, derived from <see cref="RecipeLevels"/>.</summary>
    public IReadOnlyList<int> ValidLevels =>
        RecipeLevels.Select(level => level.Level).ToArray();
}

/// <summary>One selectable Homecoming Recipe level belonging to a logical Recipe.</summary>
public sealed record RecipeLevelReferenceRecord
{
    public required int Level { get; init; }

    public required string HomecomingSourceId { get; init; }

    public required uint CraftingCost { get; init; }

    public IReadOnlyList<RecipeRequirementReferenceRecord> Requirements { get; init; } =
        Array.Empty<RecipeRequirementReferenceRecord>();
}

/// <summary>One salvage ingredient required to craft a Recipe at a specific level.</summary>
public sealed record RecipeRequirementReferenceRecord
{
    public required string SalvageItemId { get; init; }

    public required uint Quantity { get; init; }
}

/// <summary>
/// Provenance for a Homecoming Recipe source row that is not a Reference-selectable level.
/// </summary>
public sealed record RecipeExcludedSourceLevelReferenceRecord
{
    public required int Level { get; init; }

    public required string HomecomingSourceId { get; init; }

    public required uint CraftingCost { get; init; }
}

/// <summary>One concrete Homecoming boost record belonging to a logical Enhancement.</summary>
public sealed record EnhancementSourceVariantReferenceRecord
{
    public required string HomecomingSourceId { get; init; }

    public required string SourceForm { get; init; }

    /// <summary>Homecoming powers.bin icon identity when present.</summary>
    public string? Icon { get; init; }

    /// <summary>Resolved Homecoming display_help for this concrete source variant.</summary>
    public string? DisplayHelp { get; init; }

    /// <summary>Resolved Homecoming short_help for this concrete source variant when present.</summary>
    public string? ShortHelp { get; init; }

    /// <summary>When true, effective level follows player/security level (Attuned/Superior Attuned).</summary>
    public bool BoostUsePlayerLevel { get; init; }

    /// <summary>Homecoming MaxBoostLevel cap for Scale level selection.</summary>
    public int MaxBoostLevel { get; init; }

    /// <summary>Whether Enhancement Boosters may apply to this source variant.</summary>
    public bool BoostBoostable { get; init; }

    /// <summary>
    /// Canonical Scale-relevant effect facts for this source variant. Empty when
    /// no live resolver facts were promoted (for example historical-only records).
    /// </summary>
    public IReadOnlyList<EnhancementSourceVariantEffectReferenceRecord> Effects { get; init; } =
        Array.Empty<EnhancementSourceVariantEffectReferenceRecord>();
}

/// <summary>One Scale-relevant boost effect group on a concrete source variant.</summary>
public sealed record EnhancementSourceVariantEffectReferenceRecord
{
    /// <summary>Authored effect tag text (case preserved).</summary>
    public required string Tag { get; init; }

    /// <summary>Named schedule table identity from classes.bin.</summary>
    public required string Table { get; init; }

    /// <summary>Authored effect scale multiplier.</summary>
    public required float Scale { get; init; }

    /// <summary>Minimal attrib identity from the effect template when present.</summary>
    public IReadOnlyList<uint> AttribIds { get; init; } = Array.Empty<uint>();
}

/// <summary>One canonical NamedTable curve used by Enhancement help Scale resolution.</summary>
public sealed record EnhancementResolverNamedTableReferenceRecord
{
    public required string Name { get; init; }

    public required IReadOnlyList<float> Values { get; init; }
}

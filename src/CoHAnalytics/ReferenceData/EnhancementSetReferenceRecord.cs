namespace CoHAnalytics.ReferenceData;

/// <summary>Enhancement set identity in the internal reference catalog.</summary>
public sealed record EnhancementSetReferenceRecord
{
    public required string CatalogItemId { get; init; }

    public required string CurrentDisplayName { get; init; }

    public required ReferenceActiveStatus ActiveStatus { get; init; }

    /// <summary>
    /// Server-specific content availability. Required for all Enhancement Sets.
    /// Separate from <see cref="ActiveStatus"/>, which tracks authored catalog lifecycle.
    /// </summary>
    public IReadOnlyList<ReferenceServerAvailability> ServerAvailability { get; init; } =
        Array.Empty<ReferenceServerAvailability>();

    public required ReferenceVerificationStatus VerificationStatus { get; init; }

    /// <summary>Exact Homecoming boost-set identity (e.g. Absolute_Amazement).</summary>
    public string? HomecomingSetId { get; init; }

    /// <summary>Canonical Homecoming category code (e.g. ECMelee, ECToHitDeBuff).</summary>
    public string? CategoryCode { get; init; }

    /// <summary>
    /// Player-facing category presentation label. Presentation metadata only —
    /// never replaces <see cref="CategoryCode"/> as canonical identity.
    /// </summary>
    public string? CategoryDisplayText { get; init; }

    /// <summary>Canonical Homecoming rarity code when present (e.g. ECUncommon).</summary>
    public string? RarityCode { get; init; }

    /// <summary>Player-facing rarity presentation label when a message exists.</summary>
    public string? RarityDisplayText { get; init; }

    /// <summary>Homecoming set slottable minimum level from boostsets.bin.</summary>
    public int? MinimumLevel { get; init; }

    /// <summary>Homecoming set slottable maximum level from boostsets.bin.</summary>
    public int? MaximumLevel { get; init; }

    /// <summary>Canonical set-bonus tiers from boostsets.bin (empty when none).</summary>
    public IReadOnlyList<EnhancementSetBonusReferenceRecord> Bonuses { get; init; } =
        Array.Empty<EnhancementSetBonusReferenceRecord>();
}

/// <summary>One Homecoming set-bonus tier (piece-count range + auto powers).</summary>
public sealed record EnhancementSetBonusReferenceRecord
{
    public required int MinimumBoosts { get; init; }

    public required int MaximumBoosts { get; init; }

    /// <summary>Bounded semantic classification of the Requires expression.</summary>
    public required ReferenceEnhancementSetBonusRequiresPattern RequiresPattern { get; init; }

    /// <summary>
    /// Ordered Homecoming Requires RPN tokens. Empty when <see cref="RequiresPattern"/> is
    /// <see cref="ReferenceEnhancementSetBonusRequiresPattern.None"/>.
    /// </summary>
    public IReadOnlyList<string> RequiresTokens { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Resolved logical Enhancement identities for piece-gated tiers when deterministically
    /// proven. Empty for non-piece gates.
    /// </summary>
    public IReadOnlyList<string> RequiredEnhancementIds { get; init; } = Array.Empty<string>();

    public required IReadOnlyList<EnhancementSetBonusPowerReferenceRecord> AutoPowers { get; init; }
}

/// <summary>One Set_Bonus.* auto-power granted by a set-bonus tier.</summary>
public sealed record EnhancementSetBonusPowerReferenceRecord
{
    public required string HomecomingSourceId { get; init; }

    /// <summary>Resolved Homecoming display_help text when available.</summary>
    public string? DisplayHelp { get; init; }

    public bool BoostUsePlayerLevel { get; init; }

    public int MaxBoostLevel { get; init; }

    public bool BoostBoostable { get; init; }

    public IReadOnlyList<EnhancementSourceVariantEffectReferenceRecord> Effects { get; init; } =
        Array.Empty<EnhancementSourceVariantEffectReferenceRecord>();
}

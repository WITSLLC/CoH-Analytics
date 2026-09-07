namespace CoHAnalytics.ReferenceData;

/// <summary>
/// Shared Reference presentation mapping from canonical Homecoming rarity identity to UI brush resources.
/// Presentation only — never replaces canonical rarity codes in catalog data.
/// Multiple canonical identities may collapse into the same presentation family.
/// </summary>
public enum ReferenceRarityPresentationFamily
{
    Default = 0,
    Uncommon = 1,
    Rare = 2,
    VeryRare = 3
}

public static class ReferenceRarityPresentation
{
    public const string UncommonBrushKey = "Brush.ReferenceRarityUncommon";
    public const string RareBrushKey = "Brush.ReferenceRarityRare";
    public const string VeryRareBrushKey = "Brush.ReferenceRarityVeryRare";
    public const string DefaultBrushKey = "Brush.TextPrimary";

    private const string UncommonRarityCode = "ECUncommon";
    private const string RareRarityCode = "ECRare";
    private const string VeryRareRarityCode = "ECVeryRare";
    private const string AtoRarityCode = "ECATO";
    private const string SuperiorAtoRarityCode = "ECSATO";
    private const string WinterRarityCode = "ECWinter";
    private const string SuperiorWinterRarityCode = "ECSWinter";
    private const string PvpRarityCode = "ECPVP";

    public static ReferenceRarityPresentationFamily ResolveFamily(string? canonicalRarityCode) =>
        canonicalRarityCode switch
        {
            UncommonRarityCode => ReferenceRarityPresentationFamily.Uncommon,
            RareRarityCode or AtoRarityCode or WinterRarityCode or PvpRarityCode =>
                ReferenceRarityPresentationFamily.Rare,
            VeryRareRarityCode or SuperiorAtoRarityCode or SuperiorWinterRarityCode =>
                ReferenceRarityPresentationFamily.VeryRare,
            _ => ReferenceRarityPresentationFamily.Default
        };

    public static string ResolveBrushResourceKey(string? canonicalRarityCode) =>
        ResolveFamily(canonicalRarityCode) switch
        {
            ReferenceRarityPresentationFamily.Uncommon => UncommonBrushKey,
            ReferenceRarityPresentationFamily.Rare => RareBrushKey,
            ReferenceRarityPresentationFamily.VeryRare => VeryRareBrushKey,
            _ => DefaultBrushKey
        };

    /// <summary>
    /// Future Recipe Reference contract: map live Homecoming recipe tier (1–4) to the same presentation families.
    /// Tier 1 / Common resolves to <see cref="ReferenceRarityPresentationFamily.Default"/>.
    /// </summary>
    public static ReferenceRarityPresentationFamily ResolveFamilyFromRecipeTier(int recipeRarityTier) =>
        recipeRarityTier switch
        {
            2 => ReferenceRarityPresentationFamily.Uncommon,
            3 => ReferenceRarityPresentationFamily.Rare,
            4 => ReferenceRarityPresentationFamily.VeryRare,
            _ => ReferenceRarityPresentationFamily.Default
        };

    /// <summary>
    /// Maps catalog Salvage rarity labels to canonical Enhancement-style rarity codes used by presentation.
    /// </summary>
    public static string? SalvageRarityLabelToCode(string? rarityLabel)
    {
        if (string.IsNullOrWhiteSpace(rarityLabel))
        {
            return null;
        }

        return rarityLabel.Trim().Equals("Uncommon", StringComparison.OrdinalIgnoreCase)
            ? UncommonRarityCode
            : rarityLabel.Trim().Equals("Rare", StringComparison.OrdinalIgnoreCase)
                ? RareRarityCode
                : rarityLabel.Trim().Equals("Very Rare", StringComparison.OrdinalIgnoreCase)
                    ? VeryRareRarityCode
                    : null;
    }

    /// <summary>
    /// Maps catalog Recipe rarity labels to canonical rarity codes used by presentation.
    /// </summary>
    public static string? RecipeRarityLabelToCode(string? rarityLabel)
    {
        if (string.IsNullOrWhiteSpace(rarityLabel))
        {
            return null;
        }

        return rarityLabel.Trim().Equals("Uncommon", StringComparison.OrdinalIgnoreCase)
            ? UncommonRarityCode
            : rarityLabel.Trim().Equals("Rare", StringComparison.OrdinalIgnoreCase)
                ? RareRarityCode
                : rarityLabel.Trim().Equals("Very Rare", StringComparison.OrdinalIgnoreCase)
                    ? VeryRareRarityCode
                    : null;
    }
}

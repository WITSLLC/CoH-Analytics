namespace CoHAnalytics.ReferenceData;

/// <summary>
/// App-owned semantic effect-family classification for canonical Inspirations.
/// Derived from authoritative Homecoming catalog metadata — never from display-name heuristics.
/// </summary>
public enum InspirationSemanticFamily
{
    Damage = 1,
    Accuracy = 2,
    Defense = 3,
    Resistance = 4,
    Health = 5,
    Endurance = 6,
    Resurrection = 7,
    StatusProtection = 8,
    LevelShift = 9,
    Special = 10
}

/// <summary>
/// Semantic presentation metadata for a canonical Inspiration record.
/// </summary>
public readonly record struct InspirationPresentationMetadata(
    InspirationSemanticFamily PrimaryFamily,
    InspirationSemanticFamily? SecondaryFamily,
    string PresentationKey,
    string? UnmappedIconStem = null);

/// <summary>
/// Maps canonical Inspiration catalog records to semantic families and theme brush keys.
/// Presentation only — never replaces canonical catalog metadata.
/// </summary>
public static class InspirationPresentation
{
    public const string DamageBrushKey = "Brush.InspirationDamage";
    public const string AccuracyBrushKey = "Brush.InspirationAccuracy";
    public const string DefenseBrushKey = "Brush.InspirationDefense";
    public const string ResistanceBrushKey = "Brush.InspirationResistance";
    public const string HealthBrushKey = "Brush.InspirationHealth";
    public const string EnduranceBrushKey = "Brush.InspirationEndurance";
    public const string ResurrectionBrushKey = "Brush.InspirationResurrection";
    public const string StatusProtectionBrushKey = "Brush.InspirationStatusProtection";
    public const string LevelShiftBrushKey = "Brush.InspirationLevelShift";
    public const string SpecialBrushKey = "Brush.InspirationSpecial";

    private static readonly HashSet<string> SpecialCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Holiday",
        "Anniversary",
        "Special"
    };

    private static readonly HashSet<string> SpecialIconStems = new(StringComparer.OrdinalIgnoreCase)
    {
        "Anniversary",
        "Present",
        "Hambrosia",
        "Eden_Trial",
        "Teleport_Blackmarket",
        "Teleport_Wentworths",
        "Labyrinth",
        "Nightmare"
    };

    private static readonly (string Stem, InspirationSemanticFamily Primary, InspirationSemanticFamily? Secondary)[]
        IconStemRules =
        [
            ("LevelShift", InspirationSemanticFamily.LevelShift, null),
            ("Team_Dual_Health_End", InspirationSemanticFamily.Health, InspirationSemanticFamily.Endurance),
            ("Team_Dual_Def_Res", InspirationSemanticFamily.Defense, InspirationSemanticFamily.Resistance),
            ("Team_Dual_Dmg_Acc", InspirationSemanticFamily.Damage, InspirationSemanticFamily.Accuracy),
            ("Dual_Health_End", InspirationSemanticFamily.Health, InspirationSemanticFamily.Endurance),
            ("Dual_Def_Res", InspirationSemanticFamily.Defense, InspirationSemanticFamily.Resistance),
            ("Dual_Dmg_Acc", InspirationSemanticFamily.Damage, InspirationSemanticFamily.Accuracy),
            ("Team_Resist_Effects", InspirationSemanticFamily.StatusProtection, null),
            ("resist_sleep_hold", InspirationSemanticFamily.StatusProtection, null),
            ("Mes_Protect", InspirationSemanticFamily.StatusProtection, null),
            ("Damage_Resistance", InspirationSemanticFamily.Resistance, null),
            ("Team_Dmg", InspirationSemanticFamily.Damage, null),
            ("Team_Acc", InspirationSemanticFamily.Accuracy, null),
            ("Team_Def", InspirationSemanticFamily.Defense, null),
            ("Team_End", InspirationSemanticFamily.Endurance, null),
            ("Team_Health", InspirationSemanticFamily.Health, null),
            ("Team_Res", InspirationSemanticFamily.Resistance, null),
            ("Resurrection", InspirationSemanticFamily.Resurrection, null),
            ("Accuracy", InspirationSemanticFamily.Accuracy, null),
            ("Damage", InspirationSemanticFamily.Damage, null),
            ("Defense", InspirationSemanticFamily.Defense, null),
            ("Endurance", InspirationSemanticFamily.Endurance, null),
            ("Health", InspirationSemanticFamily.Health, null)
        ];

    public static InspirationPresentationMetadata Resolve(ItemReferenceRecord item)
    {
        if (ShouldUseSpecialPresentation(item))
        {
            return CreateMetadata(InspirationSemanticFamily.Special, null);
        }

        if (string.IsNullOrWhiteSpace(item.Icon))
        {
            return CreateMetadata(InspirationSemanticFamily.Special, null, unmappedIconStem: string.Empty);
        }

        var iconStem = ExtractIconStem(item.Icon);
        if (SpecialIconStems.Contains(iconStem))
        {
            return CreateMetadata(InspirationSemanticFamily.Special, null);
        }

        if (TryResolveFamiliesFromIconStem(iconStem, out var primary, out var secondary))
        {
            return CreateMetadata(primary, secondary);
        }

        return CreateMetadata(InspirationSemanticFamily.Special, null, unmappedIconStem: iconStem);
    }

    public static string ResolveBrushResourceKey(InspirationSemanticFamily family) =>
        family switch
        {
            InspirationSemanticFamily.Damage => DamageBrushKey,
            InspirationSemanticFamily.Accuracy => AccuracyBrushKey,
            InspirationSemanticFamily.Defense => DefenseBrushKey,
            InspirationSemanticFamily.Resistance => ResistanceBrushKey,
            InspirationSemanticFamily.Health => HealthBrushKey,
            InspirationSemanticFamily.Endurance => EnduranceBrushKey,
            InspirationSemanticFamily.Resurrection => ResurrectionBrushKey,
            InspirationSemanticFamily.StatusProtection => StatusProtectionBrushKey,
            InspirationSemanticFamily.LevelShift => LevelShiftBrushKey,
            _ => SpecialBrushKey
        };

    internal static string ExtractIconStem(string iconIdentity)
    {
        var trimmed = iconIdentity.Trim();
        if (trimmed.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        const string prefix = "Inspiration_";
        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[prefix.Length..];
        }

        var levelSuffixIndex = trimmed.LastIndexOf("_Lvl_", StringComparison.OrdinalIgnoreCase);
        if (levelSuffixIndex > 0)
        {
            trimmed = trimmed[..levelSuffixIndex];
        }

        return trimmed;
    }

    private static bool ShouldUseSpecialPresentation(ItemReferenceRecord item)
    {
        if (string.Equals(item.InspirationForm, "Special/Event", StringComparison.Ordinal))
        {
            return true;
        }

        return item.HomecomingCategory is not null
               && SpecialCategories.Contains(item.HomecomingCategory);
    }

    private static bool TryResolveFamiliesFromIconStem(
        string iconStem,
        out InspirationSemanticFamily primary,
        out InspirationSemanticFamily? secondary)
    {
        foreach (var rule in IconStemRules)
        {
            if (!string.Equals(iconStem, rule.Stem, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            primary = rule.Primary;
            secondary = rule.Secondary;
            return true;
        }

        primary = default;
        secondary = null;
        return false;
    }

    private static InspirationPresentationMetadata CreateMetadata(
        InspirationSemanticFamily primary,
        InspirationSemanticFamily? secondary,
        string? unmappedIconStem = null) =>
        new(
            primary,
            secondary,
            ResolveBrushResourceKey(primary),
            unmappedIconStem);
}

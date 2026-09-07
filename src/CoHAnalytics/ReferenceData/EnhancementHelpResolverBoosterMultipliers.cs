namespace CoHAnalytics.ReferenceData;

/// <summary>
/// Canonical Homecoming Enhancement Booster (+0…+5) multipliers from
/// <c>bin/boost_effect_boosters.bin</c> (Parse7 float curve).
/// </summary>
internal static class EnhancementHelpResolverBoosterMultipliers
{
    internal const int NativeCapPresentationLevel = 50;

    internal const int MaxBoostedPresentationLevel = 55;

    /// <summary>Index N corresponds to +N combines (presentation level 50+N).</summary>
    internal static readonly float[] Plus0ThroughPlus5 =
    [
        1.00f,
        1.05f,
        1.10f,
        1.15f,
        1.20f,
        1.25f
    ];

    internal static bool SupportsBoostedPresentationLevels(
        EnhancementSourceVariantReferenceRecord variant) =>
        variant.BoostBoostable
        && !variant.BoostUsePlayerLevel
        && variant.MaxBoostLevel >= NativeCapPresentationLevel;

    internal static bool IsBoostedPresentationLevel(int presentationLevel) =>
        presentationLevel > NativeCapPresentationLevel
        && presentationLevel <= MaxBoostedPresentationLevel;

    internal static bool TryGetBoosterCount(int presentationLevel, out int boosterCount)
    {
        if (!IsBoostedPresentationLevel(presentationLevel))
        {
            boosterCount = 0;
            return false;
        }

        boosterCount = presentationLevel - NativeCapPresentationLevel;
        return boosterCount >= 0 && boosterCount < Plus0ThroughPlus5.Length;
    }
}

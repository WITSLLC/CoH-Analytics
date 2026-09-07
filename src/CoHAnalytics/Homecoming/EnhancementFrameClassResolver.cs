using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Homecoming;

internal static class EnhancementFrameClassResolver
{
    internal static EnhancementFrameClass? TryResolve(
        ReferenceEnhancementFamily? enhancementFamily,
        string? sourceForm,
        string? variant,
        string? setRarityCode)
    {
        if (enhancementFamily == ReferenceEnhancementFamily.CraftedInvention)
        {
            return EnhancementFrameClass.Invention;
        }

        if (enhancementFamily == ReferenceEnhancementFamily.OriginOrTraining)
        {
            return EnhancementFrameClass.Invention;
        }

        if (enhancementFamily is ReferenceEnhancementFamily.Hamidon
            or ReferenceEnhancementFamily.DSync
            or ReferenceEnhancementFamily.Hydra
            or ReferenceEnhancementFamily.Titan
            or ReferenceEnhancementFamily.Synthetic
            or ReferenceEnhancementFamily.Yin)
        {
            return EnhancementFrameClass.Rare;
        }

        if (string.Equals(sourceForm, "Superior_Attuned", StringComparison.Ordinal))
        {
            return EnhancementFrameClass.SuperiorAttuned;
        }

        if (string.Equals(sourceForm, "Attuned", StringComparison.Ordinal))
        {
            if (string.Equals(setRarityCode, "ECUncommon", StringComparison.Ordinal))
            {
                return EnhancementFrameClass.UncommonAttuned;
            }

            if (string.Equals(setRarityCode, "ECPVP", StringComparison.OrdinalIgnoreCase))
            {
                return EnhancementFrameClass.PvPAttuned;
            }

            return EnhancementFrameClass.Attuned;
        }

        if (string.Equals(variant, "Superior", StringComparison.Ordinal))
        {
            return EnhancementFrameClass.Superior;
        }

        return setRarityCode switch
        {
            "ECUncommon" => EnhancementFrameClass.Uncommon,
            "ECRare" => EnhancementFrameClass.Rare,
            "ECVeryRare" => EnhancementFrameClass.VeryRare,
            "ECPVP" => EnhancementFrameClass.PvP,
            _ => null
        };
    }
}

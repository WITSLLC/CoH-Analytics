namespace CoHAnalytics.Homecoming;

internal static class EnhancementFrameIdentity
{
    internal static bool TryResolveHalves(
        EnhancementFrameClass frameClass,
        out string leftIdentity,
        out string rightIdentity)
    {
        leftIdentity = string.Empty;
        rightIdentity = string.Empty;

        switch (frameClass)
        {
            case EnhancementFrameClass.Invention:
                leftIdentity = "E_orgin_invention_L.tga";
                rightIdentity = "E_orgin_invention_R.tga";
                return true;
            case EnhancementFrameClass.Uncommon:
                leftIdentity = "e_orgin_uncommon_l.tga";
                rightIdentity = "e_orgin_uncommon_r.tga";
                return true;
            case EnhancementFrameClass.UncommonAttuned:
                leftIdentity = "e_orgin_uncommonattune_l.tga";
                rightIdentity = "e_orgin_uncommonattune_r.tga";
                return true;
            case EnhancementFrameClass.Rare:
                leftIdentity = "e_orgin_rare_l.tga";
                rightIdentity = "e_orgin_rare_r.tga";
                return true;
            case EnhancementFrameClass.VeryRare:
                leftIdentity = "E_orgin_uber_L.tga";
                rightIdentity = "E_orgin_uber_R.tga";
                return true;
            case EnhancementFrameClass.PvP:
                leftIdentity = "e_orgin_pvp_l.tga";
                rightIdentity = "e_orgin_pvp_r.tga";
                return true;
            case EnhancementFrameClass.PvPAttuned:
                leftIdentity = "e_orgin_pvpattune_l.tga";
                rightIdentity = "e_orgin_pvpattune_r.tga";
                return true;
            case EnhancementFrameClass.Attuned:
                leftIdentity = "E_origin_attune_L.tga";
                rightIdentity = "E_origin_attune_R.tga";
                return true;
            case EnhancementFrameClass.SuperiorAttuned:
                leftIdentity = "E_origin_superiorattune_L.tga";
                rightIdentity = "E_origin_superiorattune_R.tga";
                return true;
            case EnhancementFrameClass.Superior:
                leftIdentity = "e_orgin_superior_l.tga";
                rightIdentity = "e_orgin_superior_r.tga";
                return true;
            default:
                return false;
        }
    }
}

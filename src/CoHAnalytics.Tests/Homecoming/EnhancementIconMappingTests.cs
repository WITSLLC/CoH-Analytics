using CoHAnalytics.Homecoming;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class EnhancementIconMappingTests
{
    [Fact]
    public void PogResolver_SingleAspectCommonIo_UsesFirstNonOriginBoostType()
    {
        var boostsAllowed = new[] { "Accuracy", "Natural", "Technology", "Magic", "Mutation", "Science" };

        var pogType = EnhancementPogBoostTypeResolver.TryResolvePrimaryBoostType(boostsAllowed);

        Assert.Equal("Accuracy", pogType);
        Assert.Equal("E_POG_ACCURACY", EnhancementPogIdentity.TryResolveIdentity(pogType));
    }

    [Theory]
    [InlineData(
        new[] { "Damage", "Natural", "Technology", "Magic", "Mutation", "Science" },
        "Damage",
        "E_POG_DAMAGE")]
    [InlineData(
        new[] { "Accuracy", "Natural", "Technology", "Magic", "Mutation", "Science" },
        "Accuracy",
        "E_POG_ACCURACY")]
    [InlineData(
        new[] { "EnduranceDiscount", "Natural", "Technology", "Magic", "Mutation", "Science" },
        "EnduranceDiscount",
        "E_POG_END_DISCOUNT")]
    public void PogResolver_MultiAspectSetIo_UsesFirstNonOriginBoostTypeInBoostsAllowedOrder(
        string[] boostsAllowed,
        string expectedBoostType,
        string expectedPogIdentity)
    {
        var pogType = EnhancementPogBoostTypeResolver.TryResolvePrimaryBoostType(boostsAllowed);

        Assert.Equal(expectedBoostType, pogType);
        Assert.Equal(expectedPogIdentity, EnhancementPogIdentity.TryResolveIdentity(pogType));
    }

    [Fact]
    public void PogResolver_ProcBoostRecord_StillUsesCanonicalBoostsAllowedLeadType()
    {
        var boostsAllowed = new[] { "Damage", "Natural", "Technology", "Magic", "Mutation", "Science" };

        var pogType = EnhancementPogBoostTypeResolver.TryResolvePrimaryBoostType(boostsAllowed);

        Assert.Equal("Damage", pogType);
    }

    [Fact]
    public void FrameResolver_CommonInvention_UsesInventionFrame()
    {
        var frameClass = EnhancementFrameClassResolver.TryResolve(
            ReferenceEnhancementFamily.CraftedInvention,
            "Crafted",
            "Regular",
            null);

        Assert.Equal(EnhancementFrameClass.Invention, frameClass);
    }

    [Theory]
    [InlineData("ECUncommon", "Crafted", "Regular", EnhancementFrameClass.Uncommon)]
    [InlineData("ECUncommon", "Attuned", "Regular", EnhancementFrameClass.UncommonAttuned)]
    [InlineData("ECRare", "Crafted", "Regular", EnhancementFrameClass.Rare)]
    [InlineData("ECVeryRare", "Crafted", "Regular", EnhancementFrameClass.VeryRare)]
    [InlineData("ECPVP", "Crafted", "Regular", EnhancementFrameClass.PvP)]
    [InlineData("ECPVP", "Attuned", "Regular", EnhancementFrameClass.PvPAttuned)]
    [InlineData("ECRare", "Attuned", "Regular", EnhancementFrameClass.Attuned)]
    [InlineData("ECATO", "Attuned", "Regular", EnhancementFrameClass.Attuned)]
    [InlineData("ECWinter", "Attuned", "Regular", EnhancementFrameClass.Attuned)]
    [InlineData("ECSATO", "Superior_Attuned", "Superior", EnhancementFrameClass.SuperiorAttuned)]
    [InlineData("ECSWinter", "Superior_Attuned", "Superior", EnhancementFrameClass.SuperiorAttuned)]
    [InlineData("ECVeryRare", "Crafted", "Superior", EnhancementFrameClass.Superior)]
    public void FrameResolver_SupportedReferenceFamilies_MapToExpectedFrameClass(
        string rarityCode,
        string sourceForm,
        string variant,
        EnhancementFrameClass expected)
    {
        var frameClass = EnhancementFrameClassResolver.TryResolve(
            null,
            sourceForm,
            variant,
            rarityCode);

        Assert.Equal(expected, frameClass);
    }

    [Theory]
    [InlineData(EnhancementFrameClass.Invention, "E_orgin_invention_L.tga", "E_orgin_invention_R.tga")]
    [InlineData(EnhancementFrameClass.Uncommon, "e_orgin_uncommon_l.tga", "e_orgin_uncommon_r.tga")]
    [InlineData(EnhancementFrameClass.UncommonAttuned, "e_orgin_uncommonattune_l.tga", "e_orgin_uncommonattune_r.tga")]
    [InlineData(EnhancementFrameClass.Rare, "e_orgin_rare_l.tga", "e_orgin_rare_r.tga")]
    [InlineData(EnhancementFrameClass.VeryRare, "E_orgin_uber_L.tga", "E_orgin_uber_R.tga")]
    [InlineData(EnhancementFrameClass.PvP, "e_orgin_pvp_l.tga", "e_orgin_pvp_r.tga")]
    [InlineData(EnhancementFrameClass.PvPAttuned, "e_orgin_pvpattune_l.tga", "e_orgin_pvpattune_r.tga")]
    [InlineData(EnhancementFrameClass.Attuned, "E_origin_attune_L.tga", "E_origin_attune_R.tga")]
    [InlineData(EnhancementFrameClass.SuperiorAttuned, "E_origin_superiorattune_L.tga", "E_origin_superiorattune_R.tga")]
    [InlineData(EnhancementFrameClass.Superior, "e_orgin_superior_l.tga", "e_orgin_superior_r.tga")]
    public void FrameIdentity_SupportedClasses_ResolveLeftAndRightAssets(
        EnhancementFrameClass frameClass,
        string expectedLeft,
        string expectedRight)
    {
        Assert.True(EnhancementFrameIdentity.TryResolveHalves(frameClass, out var left, out var right));
        Assert.Equal(expectedLeft, left);
        Assert.Equal(expectedRight, right);
    }

    [Fact]
    public void FrameResolver_OriginOrTraining_UsesInventionFrame()
    {
        var frameClass = EnhancementFrameClassResolver.TryResolve(
            ReferenceEnhancementFamily.OriginOrTraining,
            "Magic",
            "Regular",
            null);

        Assert.Equal(EnhancementFrameClass.Invention, frameClass);
    }

    [Theory]
    [InlineData(ReferenceEnhancementFamily.Hamidon)]
    [InlineData(ReferenceEnhancementFamily.DSync)]
    [InlineData(ReferenceEnhancementFamily.Hydra)]
    [InlineData(ReferenceEnhancementFamily.Titan)]
    [InlineData(ReferenceEnhancementFamily.Synthetic)]
    [InlineData(ReferenceEnhancementFamily.Yin)]
    public void FrameResolver_SpecialRaidFamilies_UseRareFrame(ReferenceEnhancementFamily family)
    {
        var frameClass = EnhancementFrameClassResolver.TryResolve(
            family,
            "Hamidon",
            "Regular",
            null);

        Assert.Equal(EnhancementFrameClass.Rare, frameClass);
    }

    [Fact]
    public void FrameResolver_UnknownRarity_ReturnsNull()
    {
        var frameClass = EnhancementFrameClassResolver.TryResolve(null, "Crafted", "Regular", "ECUnknown");

        Assert.Null(frameClass);
    }

    [Fact]
    public void PogIdentity_UnknownBoostType_ReturnsNull()
    {
        Assert.Null(EnhancementPogIdentity.TryResolveIdentity("Hamidon"));
    }
}

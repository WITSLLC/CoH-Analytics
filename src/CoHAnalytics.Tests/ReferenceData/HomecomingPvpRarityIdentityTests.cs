using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

public sealed class HomecomingPvpRarityIdentityTests
{
    private static readonly string[] ExpectedCurrentPvpSetIds =
    [
        "Gladiators_Strike",
        "Fury_of_the_Gladiator",
        "Gladiators_Javelin",
        "Javelin_Volley",
        "Experienced_Marksman",
        "Panacea",
        "Shield_Wall",
        "Gladiators_Armor",
        "Gladiators_Net"
    ];

    [Fact]
    public void Production_catalog_current_homecoming_pvp_sets_use_canonical_rarity_identity()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var currentSets = catalog.GetEnhancementSets(ReferenceCatalogQueryScope.CurrentHomecoming);
        var pvpSets = currentSets.Values
            .Where(set => set.RarityCode == "ECPVP")
            .OrderBy(set => set.CatalogItemId, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(9, pvpSets.Length);
        Assert.Equal(
            ExpectedCurrentPvpSetIds.OrderBy(value => value, StringComparer.Ordinal),
            pvpSets.Select(set => set.HomecomingSetId).OrderBy(value => value, StringComparer.Ordinal));
        Assert.All(pvpSets, set => Assert.Equal("PvP", set.RarityDisplayText));
        Assert.DoesNotContain(
            currentSets.Values,
            set => string.Equals(set.RarityCode, "ECPvP", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_catalog_gladiators_strike_resolves_pvp_rarity_label()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var set = catalog.GetEnhancementSets(ReferenceCatalogQueryScope.CurrentHomecoming)
            .Values
            .Single(value => value.HomecomingSetId == "Gladiators_Strike");

        Assert.Equal("SET-00012", set.CatalogItemId);
        Assert.Equal("Gladiator's Strike", set.CurrentDisplayName);
        Assert.Equal("ECPVP", set.RarityCode);
        Assert.Equal("PvP", set.RarityDisplayText);
    }

    [Fact]
    public void Production_catalog_historical_sets_do_not_carry_pvp_rarity_identity()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var historicalPvpSets = catalog.GetEnhancementSets(ReferenceCatalogQueryScope.AllHomecomingIdentities)
            .Values
            .Where(set => ReferenceServerAvailabilitySupport.IsHistoricalHomecoming(set.ServerAvailability))
            .Where(set => string.Equals(set.RarityCode, "ECPvP", StringComparison.Ordinal)
                || string.Equals(set.RarityCode, "ECPVP", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(historicalPvpSets);
    }

    [Fact]
    public void Shared_rarity_presentation_maps_ecpvp_to_auction_house_orange_family()
    {
        Assert.Equal(
            ReferenceRarityPresentationFamily.Rare,
            ReferenceRarityPresentation.ResolveFamily("ECPVP"));
        Assert.Equal(
            ReferenceRarityPresentation.RareBrushKey,
            ReferenceRarityPresentation.ResolveBrushResourceKey("ECPVP"));
    }
}

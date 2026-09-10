using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class CharacterBuildSetAnalysisServiceTests
{
    private static readonly IItemReferenceCatalog Catalog =
        ItemReferenceCatalogFactory.LoadEmbeddedProduction();

    [Fact]
    public void Analyze_counts_distinct_catalog_pieces_and_uses_canonical_earned_bonus_help()
    {
        var scirocco = Catalog.GetEnhancements(ReferenceCatalogQueryScope.CurrentHomecoming)
            .Where(item => item.EnhancementSetId == "SET-00018")
            .Take(4)
            .Select(item => SourceToken(item.SourceVariants.First(variant => variant.SourceForm == "Attuned").HomecomingSourceId))
            .ToArray();
        var reactive = Catalog.GetEnhancements(ReferenceCatalogQueryScope.CurrentHomecoming)
            .Where(item => item.EnhancementSetId == "SET-00137")
            .Take(3)
            .Select(item => SourceToken(item.SourceVariants.First().HomecomingSourceId))
            .ToArray();

        var snapshot = Parse(string.Join(Environment.NewLine, scirocco.Concat(reactive)
            .Select(token => $"    {token} (1)")));
        var service = new CharacterBuildSetAnalysisService(
            Catalog,
            ItemReferenceCatalogFactory.CreateEmbeddedProductionResolver());

        var result = service.Analyze(snapshot);

        var dervish = Assert.Single(result.Sets, set => set.DisplayName == "Scirocco's Dervish");
        Assert.Equal("4 pieces", dervish.PieceCountLabel);
        Assert.Equal(3, dervish.EarnedBonuses.Count);
        Assert.Contains("Improves your Regeneration", dervish.EarnedBonuses[0].HelpLines.Single());

        var armor = Assert.Single(result.Sets, set => set.DisplayName == "Reactive Armor");
        Assert.Equal("3 pieces", armor.PieceCountLabel);
        Assert.Equal(2, armor.EarnedBonuses.Count);
    }

    [Fact]
    public void Analyze_ignores_empty_and_non_set_items()
    {
        var result = new CharacterBuildSetAnalysisService(Catalog).Analyze(Parse("    EMPTY"));

        Assert.True(result.HasNoSets);
        Assert.Empty(result.Sets);
    }

    private static HomecomingBuildLayoutSnapshot Parse(string slots)
    {
        var content = $"Beta Hero: Level 38 Magic Class_Brute\nLevel 1: Brute_Melee Fiery_Melee Scorch\n{slots}";
        Assert.True(HomecomingBuildLayoutParser.TryParse(content, out var snapshot));
        return snapshot;
    }

    private static string SourceToken(string sourceId)
    {
        var separator = sourceId.LastIndexOf('.');
        return sourceId[(separator + 1)..];
    }
}

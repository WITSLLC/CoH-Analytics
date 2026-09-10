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
        Assert.NotEmpty(result.SummaryBonuses);
    }

    [Fact]
    public void Analyze_aggregates_identical_canonical_bonuses_and_keeps_different_strengths_separate()
    {
        var tokens = SetTokens("SET-00011", 5)
            .Concat(SetTokens("SET-00020", 5))
            .Concat(SetTokens("SET-00009", 5));

        var result = CreateService().Analyze(ParseTokens(tokens));

        var ultimateRecharge = Assert.Single(result.SummaryBonuses, bonus =>
            bonus.CanonicalIdentity == "Set_Bonus.Set_Bonus.Improved_Recharge_Time_7");
        Assert.Equal(2, ultimateRecharge.Count);
        Assert.Equal("2×", ultimateRecharge.CountLabel);
        Assert.Contains("10%", ultimateRecharge.DisplayText, StringComparison.Ordinal);
        Assert.Contains(result.SummaryBonuses, bonus =>
            bonus.CanonicalIdentity == "Set_Bonus.Set_Bonus.Improved_Recharge_Time_3"
            && bonus.Count == 1);

        Assert.Equal(3, result.Sets.Count);
        Assert.All(result.Sets, set => Assert.Equal(5, set.PieceCount));
    }

    [Fact]
    public void Analyze_only_includes_earned_thresholds_in_summary()
    {
        var result = CreateService().Analyze(ParseTokens(SetTokens("SET-00018", 1)));

        Assert.True(result.HasNoSummaryBonuses);
        Assert.Empty(result.SummaryBonuses);
        Assert.Empty(Assert.Single(result.Sets).EarnedBonuses);
    }

    [Fact]
    public void Analyze_separates_authoritative_piece_gated_global_bonuses()
    {
        Assert.True(Catalog.TryGetById("ENH-00935", out var requiredPiece));

        var result = CreateService().Analyze(ParseTokens(
            [SourceToken(requiredPiece.SourceVariants.First().HomecomingSourceId)]));

        var global = Assert.Single(result.GlobalBonuses);
        Assert.Equal("Set_Bonus.Global_Bonus.Commanding_Presence", global.CanonicalIdentity);
        Assert.Equal(1, global.Count);
        Assert.Empty(result.SummaryBonuses);
        Assert.Single(Assert.Single(result.Sets).EarnedBonuses);
    }

    [Fact]
    public void Analyze_preserves_pvp_classification_in_both_views()
    {
        var result = CreateService().Analyze(ParseTokens(SetTokens("SET-00012", 6)));

        var set = Assert.Single(result.Sets);
        Assert.Contains(set.EarnedBonuses, bonus => bonus.ConditionLabel == "PvP only");
        Assert.Contains(result.SummaryBonuses, bonus => bonus.ConditionLabel == "PvP only");
    }

    [Fact]
    public void Analyze_ignores_empty_and_non_set_items()
    {
        var result = new CharacterBuildSetAnalysisService(Catalog).Analyze(Parse("    EMPTY"));

        Assert.True(result.HasNoSets);
        Assert.True(result.HasNoSummaryBonuses);
        Assert.Empty(result.Sets);
        Assert.Empty(result.SummaryBonuses);
        Assert.Empty(result.GlobalBonuses);
    }

    private static CharacterBuildSetAnalysisService CreateService() =>
        new(Catalog, ItemReferenceCatalogFactory.CreateEmbeddedProductionResolver());

    private static IEnumerable<string> SetTokens(string setId, int count) =>
        Catalog.GetEnhancements(ReferenceCatalogQueryScope.CurrentHomecoming)
            .Where(item => item.EnhancementSetId == setId)
            .Take(count)
            .Select(item => SourceToken(item.SourceVariants.First().HomecomingSourceId));

    private static HomecomingBuildLayoutSnapshot ParseTokens(IEnumerable<string> tokens) =>
        Parse(string.Join(Environment.NewLine, tokens.Select(token => $"    {token} (50)")));

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

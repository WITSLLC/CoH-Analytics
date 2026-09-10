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
        Assert.Equal(7, result.TotalEnhancementCount);
        Assert.Equal(2, result.SetCount);
        Assert.True(result.SetBonusCount > 0);
        Assert.Equal(2, result.IncompleteSetCount);
        Assert.Equal("4 / 6 pieces", dervish.PieceProgressLabel);
        Assert.False(dervish.IsComplete);
        Assert.NotEmpty(dervish.CategoryLabel);
        Assert.Equal(dervish.EarnedBonuses.Count, dervish.BonusRows.Count);
        Assert.All(dervish.BonusRows, row => Assert.False(string.IsNullOrWhiteSpace(row.Title)));
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
        Assert.Equal("Ultimate Improved Recharge Time Bonus", ultimateRecharge.Title);
        Assert.Contains("10%", ultimateRecharge.DetailText, StringComparison.Ordinal);
        Assert.Equal("2 sets", ultimateRecharge.SourceLabel);
        Assert.Contains("Armageddon", ultimateRecharge.SourceTooltip, StringComparison.Ordinal);
        Assert.Contains("Hecatomb", ultimateRecharge.SourceTooltip, StringComparison.Ordinal);
        Assert.Contains(result.SummaryBonuses, bonus =>
            bonus.CanonicalIdentity == "Set_Bonus.Set_Bonus.Improved_Recharge_Time_3"
            && bonus.Count == 1);

        Assert.Equal(3, result.Sets.Count);
        Assert.All(result.Sets, set => Assert.Equal(5, set.PieceCount));
    }

    [Fact]
    public void Analyze_summary_prefers_localized_title_and_keeps_help_as_detail()
    {
        var result = CreateService().Analyze(ParseTokens(SetTokens("SET-00018", 4)));

        var regeneration = Assert.Single(
            result.SummaryBonuses,
            bonus => bonus.CanonicalIdentity == "Set_Bonus.Set_Bonus.Improved_Regeneration_4");
        Assert.Equal("Large Improved Regeneration Bonus", regeneration.Title);
        Assert.Contains("Regeneration", regeneration.DetailText, StringComparison.Ordinal);
        Assert.DoesNotContain("Set_Bonus.", regeneration.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_global_bonuses_use_localized_titles_when_supported()
    {
        Assert.True(Catalog.TryGetById("ENH-00935", out var requiredPiece));

        var result = CreateService().Analyze(ParseTokens(
            [SourceToken(requiredPiece.SourceVariants.First().HomecomingSourceId)]));

        var global = Assert.Single(result.GlobalBonuses);
        Assert.Equal("Commanding Presence", global.Title);
        Assert.Contains("Taunt", global.DetailText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_different_canonical_bonuses_with_similar_help_do_not_merge()
    {
        var result = CreateService().Analyze(ParseTokens(
            SetTokens("SET-00011", 5)
                .Concat(SetTokens("SET-00020", 5))
                .Concat(SetTokens("SET-00009", 5))));

        var ultimate = Assert.Single(
            result.SummaryBonuses,
            bonus => bonus.CanonicalIdentity == "Set_Bonus.Set_Bonus.Improved_Recharge_Time_7");
        var large = Assert.Single(
            result.SummaryBonuses,
            bonus => bonus.CanonicalIdentity == "Set_Bonus.Set_Bonus.Improved_Recharge_Time_3");
        Assert.Equal(2, ultimate.Count);
        Assert.Equal(1, large.Count);
        Assert.NotEqual(ultimate.Title, large.Title);
    }

    [Fact]
    public void Analyze_incomplete_presentation_fields_use_safe_fallbacks_without_internal_ids()
    {
        const string catalogJson = """
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
              },
              "items": [
                {
                  "catalogItemId": "ENH-90002",
                  "family": "Enhancement",
                  "subtype": "SetIO",
                  "currentDisplayName": "Fixture Set Piece A",
                  "activeStatus": "Active",
                  "serverAvailability": [
                    { "serverKey": "Homecoming", "status": "Current" }
                  ],
                  "verificationStatus": "VerifiedMultiSource",
                  "enhancementSetId": "SET-90002",
                  "sourceVariants": [
                    {
                      "homecomingSourceId": "Boosts.Crafted_Fixture_Set_A.Crafted_Fixture_Set_A",
                      "sourceForm": "Crafted"
                    }
                  ]
                },
                {
                  "catalogItemId": "ENH-90003",
                  "family": "Enhancement",
                  "subtype": "SetIO",
                  "currentDisplayName": "Fixture Set Piece B",
                  "activeStatus": "Active",
                  "serverAvailability": [
                    { "serverKey": "Homecoming", "status": "Current" }
                  ],
                  "verificationStatus": "VerifiedMultiSource",
                  "enhancementSetId": "SET-90002",
                  "sourceVariants": [
                    {
                      "homecomingSourceId": "Boosts.Crafted_Fixture_Set_B.Crafted_Fixture_Set_B",
                      "sourceForm": "Crafted"
                    }
                  ]
                }
              ],
              "aliases": [],
              "enhancementSets": [
                {
                  "catalogItemId": "SET-90002",
                  "currentDisplayName": "Fixture Set",
                  "activeStatus": "Active",
                  "serverAvailability": [
                    { "serverKey": "Homecoming", "status": "Current" }
                  ],
                  "verificationStatus": "VerifiedMultiSource",
                  "bonuses": [
                    {
                      "minimumBoosts": 2,
                      "maximumBoosts": 6,
                      "requiresPattern": "None",
                      "requiresTokens": [],
                      "requiredEnhancementIds": [],
                      "autoPowers": [
                        {
                          "homecomingSourceId": "Set_Bonus.Set_Bonus.Test_Bonus",
                          "displayHelp": "Improves your Recovery by 4%."
                        },
                        {
                          "homecomingSourceId": "Set_Bonus.Set_Bonus.Name_Only",
                          "displayName": "Localized Name-Only Bonus"
                        },
                        {
                          "homecomingSourceId": "Set_Bonus.Set_Bonus.No_Presentation"
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """;

        var catalog = ItemReferenceCatalogFactory.LoadFromString(catalogJson);
        Assert.True(catalog.IsLoaded, catalog.LoadFailureReason);
        var result = new CharacterBuildSetAnalysisService(catalog).Analyze(ParseTokens([
            "Crafted_Fixture_Set_A",
            "Crafted_Fixture_Set_B"
        ]));

        Assert.Equal(2, result.SummaryBonuses.Count);
        var bonus = Assert.Single(result.SummaryBonuses, item =>
            item.CanonicalIdentity == "Set_Bonus.Set_Bonus.Test_Bonus");
        Assert.Equal("Improves your Recovery by 4%.", bonus.Title);
        Assert.Null(bonus.DetailText);
        Assert.DoesNotContain("Set_Bonus.", bonus.Title, StringComparison.Ordinal);
        var nameOnly = Assert.Single(result.SummaryBonuses, item =>
            item.CanonicalIdentity == "Set_Bonus.Set_Bonus.Name_Only");
        Assert.Equal("Localized Name-Only Bonus", nameOnly.Title);
        Assert.Null(nameOnly.DetailText);
        Assert.DoesNotContain(result.SummaryBonuses, item =>
            item.CanonicalIdentity == "Set_Bonus.Set_Bonus.No_Presentation");
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
        Assert.Equal(0, result.TotalEnhancementCount);
        Assert.Equal(0, result.SetCount);
        Assert.Equal(0, result.IncompleteSetCount);
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

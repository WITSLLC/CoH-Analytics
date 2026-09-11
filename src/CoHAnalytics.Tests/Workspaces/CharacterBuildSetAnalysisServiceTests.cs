using System.Text;
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
    public void Analyze_excludes_pvp_only_from_summary_but_surfaces_it_in_pvp_tab_and_by_set()
    {
        var result = CreateService().Analyze(ParseTokens(SetTokens("SET-00012", 6)));

        var set = Assert.Single(result.Sets);
        Assert.Contains(set.EarnedBonuses, bonus => bonus.ConditionLabel == "PvP only");
        Assert.DoesNotContain(result.SummaryBonuses, bonus => bonus.ConditionLabel == "PvP only");
        Assert.All(result.SummaryBonuses, bonus => Assert.NotEqual("PvP only", bonus.ConditionLabel));
        Assert.NotEmpty(result.PvpBonuses);
        Assert.All(result.PvpBonuses, bonus => Assert.Equal("PvP only", bonus.ConditionLabel));
        Assert.True(result.HasPvpBonuses);
        Assert.Equal(result.PvpBonuses.Sum(bonus => bonus.Count), result.PvpBonusCount);
    }

    [Fact]
    public void Analyze_counts_repeated_set_bonus_instances_once_per_power()
    {
        var scirocco = SetTokens("SET-00018", 4).ToArray();

        var result = CreateService().Analyze(ParsePowers(scirocco, scirocco));

        Assert.Equal(2, result.Sets.Count);
        Assert.Equal(1, result.SetCount);
        Assert.All(result.Sets, set => Assert.Equal("Scirocco's Dervish", set.DisplayName));
        var regeneration = Assert.Single(
            result.SummaryBonuses,
            bonus => bonus.CanonicalIdentity == "Set_Bonus.Set_Bonus.Improved_Regeneration_4");
        Assert.Equal(2, regeneration.Count);
        Assert.Equal("2×", regeneration.CountLabel);
        Assert.Equal(result.SummaryBonuses.Sum(bonus => bonus.Count), result.SetBonusCount);
    }

    [Fact]
    public void Analyze_identical_global_enhancement_in_three_powers_aggregates_to_one_row_count_three()
    {
        var lotg = GlobalPieceToken("ENH-00810");

        var result = CreateService().Analyze(ParsePowers([lotg], [lotg], [lotg]));

        var global = Assert.Single(result.GlobalBonuses);
        Assert.Equal("Set_Bonus.Global_Bonus.Luck_of_the_Gambler", global.CanonicalIdentity);
        Assert.Equal(3, global.Count);
        Assert.Equal("3×", global.CountLabel);
        Assert.Equal(3, result.GlobalBonusCount);
        Assert.Empty(result.SummaryBonuses);
    }

    [Fact]
    public void Analyze_two_identical_globals_aggregate_to_one_row_count_two()
    {
        var lotg = GlobalPieceToken("ENH-00810");

        var result = CreateService().Analyze(ParsePowers([lotg], [lotg]));

        var global = Assert.Single(result.GlobalBonuses);
        Assert.Equal(2, global.Count);
        Assert.Equal(2, result.GlobalBonusCount);
    }

    [Fact]
    public void Analyze_different_global_identities_remain_separate()
    {
        var lotg = GlobalPieceToken("ENH-00810");
        var commanding = GlobalPieceToken("ENH-00935");

        var result = CreateService().Analyze(ParsePowers([lotg], [commanding]));

        Assert.Equal(2, result.GlobalBonuses.Count);
        Assert.Contains(result.GlobalBonuses, bonus =>
            bonus.CanonicalIdentity == "Set_Bonus.Global_Bonus.Luck_of_the_Gambler" && bonus.Count == 1);
        Assert.Contains(result.GlobalBonuses, bonus =>
            bonus.CanonicalIdentity == "Set_Bonus.Global_Bonus.Commanding_Presence" && bonus.Count == 1);
    }

    [Fact]
    public void Analyze_applies_rule_of_five_cap_to_repeated_bonuses()
    {
        var scirocco = SetTokens("SET-00018", 4).ToArray();

        var result = CreateService().Analyze(
            ParsePowers(scirocco, scirocco, scirocco, scirocco, scirocco, scirocco));

        Assert.Equal(6, result.Sets.Count);
        var regeneration = Assert.Single(
            result.SummaryBonuses,
            bonus => bonus.CanonicalIdentity == "Set_Bonus.Set_Bonus.Improved_Regeneration_4");
        Assert.Equal(5, regeneration.Count);
        Assert.Equal("5×", regeneration.CountLabel);
    }

    [Fact]
    public void Analyze_reconciles_hells_vengence_fixture_with_in_game_bonus_counts()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory, "Services", "Fixtures", "hells-vengence-build-layout.txt");
        Assert.True(HomecomingBuildLayoutParser.TryParse(File.ReadAllText(fixturePath), out var snapshot));

        var result = CreateService().Analyze(snapshot);

        // Authoritative counts taken directly from the in-game character info screenshot.
        Assert.Equal(58, result.SetBonusCount);
        AssertSummaryCount(result, "Large Improved Regeneration Bonus", 5);
        AssertSummaryCount(result, "Large Improved Recovery Bonus", 4);
        AssertSummaryCount(result, "Ultimate Accuracy Bonus", 4);
        AssertSummaryCount(result, "Ultimate Fire, Cold and Mez Resistance", 4);
        AssertSummaryCount(result, "Ultimate Improved Recharge Time Bonus", 4);
        AssertSummaryCount(result, "Ultimate Improved Recovery Bonus", 4);
        AssertSummaryCount(result, "Large Accuracy Bonus", 3);
        AssertSummaryCount(result, "Large Improved Movement Bonus", 3);
        AssertSummaryCount(result, "Large Increased Health Bonus", 3);
        AssertSummaryCount(result, "Small Increased Health Bonus", 3);
        AssertSummaryCount(result, "Large Increased Fire/Cold/AoE Def Bonus", 1);

        // Global Bonuses: three distinct rows, six applied instances.
        Assert.Equal(3, result.GlobalBonuses.Count);
        Assert.Equal(6, result.GlobalBonusCount);
        Assert.Equal(
            3,
            Assert.Single(result.GlobalBonuses, bonus => bonus.Title == "Luck of the Gambler: Recharge Speed").Count);
        Assert.Equal(
            2,
            Assert.Single(result.GlobalBonuses, bonus => bonus.Title == "Blessing of the Zephyr: Knockback Protection").Count);
        Assert.Equal(
            1,
            Assert.Single(result.GlobalBonuses, bonus => bonus.Title == "Aegis: Psionic/Status Resistance").Count);

        // PvP-only bonuses are excluded from the PvE Summary but surfaced in the PvP tab.
        Assert.Equal(2, result.PvpBonusCount);
        Assert.NotEmpty(result.PvpBonuses);
        Assert.All(result.PvpBonuses, bonus =>
            Assert.DoesNotContain(result.SummaryBonuses, summary => summary.Title == bonus.Title));
    }

    private static void AssertSummaryCount(CharacterBuildSetAnalysis result, string title, int expected) =>
        Assert.Equal(expected, Assert.Single(result.SummaryBonuses, bonus => bonus.Title == title).Count);

    [Fact]
    public void Analyze_never_exposes_internal_identifiers_in_user_facing_output()
    {
        var scirocco = SetTokens("SET-00018", 4).ToArray();
        var lotg = GlobalPieceToken("ENH-00810");

        var result = CreateService().Analyze(ParsePowers(scirocco, [lotg]));

        foreach (var bonus in result.SummaryBonuses.Concat(result.GlobalBonuses).Concat(result.PvpBonuses))
        {
            AssertNoInternalIdentifiers(bonus.Title);
            AssertNoInternalIdentifiers(bonus.DetailText);
            AssertNoInternalIdentifiers(bonus.SourceLabel);
        }

        foreach (var row in result.Sets.SelectMany(set => set.BonusRows))
        {
            AssertNoInternalIdentifiers(row.Title);
            AssertNoInternalIdentifiers(row.DetailText);
        }
    }

    private static void AssertNoInternalIdentifiers(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Assert.DoesNotContain("Set_Bonus.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Boosts.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ENH-", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SET-", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_six_identical_stacking_globals_are_not_capped_by_rule_of_five()
    {
        var lotg = GlobalPieceToken("ENH-00810");

        var result = CreateService().Analyze(ParsePowers([lotg], [lotg], [lotg], [lotg], [lotg], [lotg]));

        var global = Assert.Single(result.GlobalBonuses);
        Assert.Equal(6, global.Count);
        Assert.Equal("6×", global.CountLabel);
        Assert.Equal(6, result.GlobalBonusCount);
    }

    [Fact]
    public void Analyze_pvp_set_bonuses_still_follow_rule_of_five()
    {
        var pieces = SetTokens("SET-00012", 2).ToArray();

        var result = CreateService().Analyze(
            ParsePowers(pieces, pieces, pieces, pieces, pieces, pieces));

        var pvp = Assert.Single(
            result.PvpBonuses,
            bonus => bonus.CanonicalIdentity == "Set_Bonus.PVP_Set_Bonus.Increased_Endurance_4");
        Assert.Equal(5, pvp.Count);
        Assert.DoesNotContain(result.SummaryBonuses, bonus => bonus.ConditionLabel == "PvP only");
    }

    [Fact]
    public void Analyze_overview_counts_distinct_sets_when_the_same_set_appears_in_multiple_powers()
    {
        var lotg = GlobalPieceToken("ENH-00810");

        var result = CreateService().Analyze(ParsePowers([lotg], [lotg], [lotg]));

        Assert.Equal(3, result.Sets.Count);
        Assert.Equal(1, result.SetCount);
        Assert.Equal(1, result.IncompleteSetCount);
        Assert.All(result.Sets, set => Assert.False(set.IsComplete));
    }

    [Fact]
    public void Analyze_empty_auto_power_help_does_not_shift_later_help_text()
    {
        const string catalogJson = """
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
              },
              "items": [
                {
                  "catalogItemId": "ENH-90010",
                  "family": "Enhancement",
                  "subtype": "SetIO",
                  "currentDisplayName": "Pairing Piece A",
                  "activeStatus": "Active",
                  "serverAvailability": [
                    { "serverKey": "Homecoming", "status": "Current" }
                  ],
                  "verificationStatus": "VerifiedMultiSource",
                  "enhancementSetId": "SET-90010",
                  "sourceVariants": [
                    {
                      "homecomingSourceId": "Boosts.Crafted_Pairing_A.Crafted_Pairing_A",
                      "sourceForm": "Crafted"
                    }
                  ]
                },
                {
                  "catalogItemId": "ENH-90011",
                  "family": "Enhancement",
                  "subtype": "SetIO",
                  "currentDisplayName": "Pairing Piece B",
                  "activeStatus": "Active",
                  "serverAvailability": [
                    { "serverKey": "Homecoming", "status": "Current" }
                  ],
                  "verificationStatus": "VerifiedMultiSource",
                  "enhancementSetId": "SET-90010",
                  "sourceVariants": [
                    {
                      "homecomingSourceId": "Boosts.Crafted_Pairing_B.Crafted_Pairing_B",
                      "sourceForm": "Crafted"
                    }
                  ]
                }
              ],
              "aliases": [],
              "enhancementSets": [
                {
                  "catalogItemId": "SET-90010",
                  "currentDisplayName": "Pairing Set",
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
                          "homecomingSourceId": "Set_Bonus.Set_Bonus.Empty_Help",
                          "displayName": "Name-Only First Bonus"
                        },
                        {
                          "homecomingSourceId": "Set_Bonus.Set_Bonus.Kept_Help",
                          "displayName": "Kept Second Bonus",
                          "displayHelp": "Second bonus keeps this help."
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
            "Crafted_Pairing_A",
            "Crafted_Pairing_B"
        ]));

        var first = Assert.Single(result.SummaryBonuses, bonus =>
            bonus.CanonicalIdentity == "Set_Bonus.Set_Bonus.Empty_Help");
        Assert.Equal("Name-Only First Bonus", first.Title);
        Assert.Null(first.DetailText);

        var second = Assert.Single(result.SummaryBonuses, bonus =>
            bonus.CanonicalIdentity == "Set_Bonus.Set_Bonus.Kept_Help");
        Assert.Equal("Kept Second Bonus", second.Title);
        Assert.Equal("Second bonus keeps this help.", second.DetailText);
        Assert.DoesNotContain(result.SummaryBonuses, bonus =>
            bonus.Title == "Kept Second Bonus" && bonus.DetailText is null);
    }

    [Fact]
    public void Analyze_other_requires_pattern_is_not_treated_as_unconditional()
    {
        const string catalogJson = """
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
              },
              "items": [
                {
                  "catalogItemId": "ENH-90020",
                  "family": "Enhancement",
                  "subtype": "SetIO",
                  "currentDisplayName": "Conditional Piece A",
                  "activeStatus": "Active",
                  "serverAvailability": [
                    { "serverKey": "Homecoming", "status": "Current" }
                  ],
                  "verificationStatus": "VerifiedMultiSource",
                  "enhancementSetId": "SET-90020",
                  "sourceVariants": [
                    {
                      "homecomingSourceId": "Boosts.Crafted_Conditional_A.Crafted_Conditional_A",
                      "sourceForm": "Crafted"
                    }
                  ]
                },
                {
                  "catalogItemId": "ENH-90021",
                  "family": "Enhancement",
                  "subtype": "SetIO",
                  "currentDisplayName": "Conditional Piece B",
                  "activeStatus": "Active",
                  "serverAvailability": [
                    { "serverKey": "Homecoming", "status": "Current" }
                  ],
                  "verificationStatus": "VerifiedMultiSource",
                  "enhancementSetId": "SET-90020",
                  "sourceVariants": [
                    {
                      "homecomingSourceId": "Boosts.Crafted_Conditional_B.Crafted_Conditional_B",
                      "sourceForm": "Crafted"
                    }
                  ]
                }
              ],
              "aliases": [],
              "enhancementSets": [
                {
                  "catalogItemId": "SET-90020",
                  "currentDisplayName": "Conditional Set",
                  "activeStatus": "Active",
                  "serverAvailability": [
                    { "serverKey": "Homecoming", "status": "Current" }
                  ],
                  "verificationStatus": "VerifiedMultiSource",
                  "bonuses": [
                    {
                      "minimumBoosts": 2,
                      "maximumBoosts": 6,
                      "requiresPattern": "Other",
                      "requiresTokens": ["FutureToken?", "1", ">="],
                      "requiredEnhancementIds": [],
                      "autoPowers": [
                        {
                          "homecomingSourceId": "Set_Bonus.Set_Bonus.Conditional_Bonus",
                          "displayName": "Conditional Named Bonus",
                          "displayHelp": "Only applies when an unknown condition is met."
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
            "Crafted_Conditional_A",
            "Crafted_Conditional_B"
        ]));

        Assert.Empty(result.SummaryBonuses);
        Assert.Empty(result.PvpBonuses);
        Assert.Empty(result.GlobalBonuses);
        var row = Assert.Single(Assert.Single(result.Sets).BonusRows);
        Assert.Equal("Conditional Named Bonus", row.Title);
        Assert.Equal("Conditional", row.ConditionLabel);
        Assert.Contains("unknown condition", row.DetailText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_normalizes_markup_in_build_analysis_detail_text()
    {
        const string catalogJson = """
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
              },
              "items": [
                {
                  "catalogItemId": "ENH-90030",
                  "family": "Enhancement",
                  "subtype": "SetIO",
                  "currentDisplayName": "Markup Piece A",
                  "activeStatus": "Active",
                  "serverAvailability": [
                    { "serverKey": "Homecoming", "status": "Current" }
                  ],
                  "verificationStatus": "VerifiedMultiSource",
                  "enhancementSetId": "SET-90030",
                  "sourceVariants": [
                    {
                      "homecomingSourceId": "Boosts.Crafted_Markup_A.Crafted_Markup_A",
                      "sourceForm": "Crafted"
                    }
                  ]
                },
                {
                  "catalogItemId": "ENH-90031",
                  "family": "Enhancement",
                  "subtype": "SetIO",
                  "currentDisplayName": "Markup Piece B",
                  "activeStatus": "Active",
                  "serverAvailability": [
                    { "serverKey": "Homecoming", "status": "Current" }
                  ],
                  "verificationStatus": "VerifiedMultiSource",
                  "enhancementSetId": "SET-90030",
                  "sourceVariants": [
                    {
                      "homecomingSourceId": "Boosts.Crafted_Markup_B.Crafted_Markup_B",
                      "sourceForm": "Crafted"
                    }
                  ]
                }
              ],
              "aliases": [],
              "enhancementSets": [
                {
                  "catalogItemId": "SET-90030",
                  "currentDisplayName": "Markup Set",
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
                          "homecomingSourceId": "Set_Bonus.Set_Bonus.Markup_Bonus",
                          "displayName": "Markup Named Bonus",
                          "displayHelp": "Damage over time.<br><br><color #cfc95>Recharge: Very Fast</color>"
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
            "Crafted_Markup_A",
            "Crafted_Markup_B"
        ]));

        var bonus = Assert.Single(result.SummaryBonuses);
        Assert.Equal("Markup Named Bonus", bonus.Title);
        Assert.Contains("Damage over time.", bonus.DetailText, StringComparison.Ordinal);
        Assert.Contains("Recharge: Very Fast", bonus.DetailText, StringComparison.Ordinal);
        Assert.DoesNotContain("<br", bonus.DetailText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<color", bonus.DetailText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<br", Assert.Single(result.Sets).BonusRows.Single().DetailText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_uses_localized_power_display_name_with_token_fallback()
    {
        var scirocco = SetTokens("SET-00018", 4).ToArray();
        var catalog = new FakePowerCatalog(
        [
            new HomecomingPowerReference(
                "Pool",
                "Fighting",
                "Power_0",
                "Fighting",
                "Smoke",
                null,
                false,
                false,
                HomecomingPowerType.Click)
        ]);

        var named = new CharacterBuildSetAnalysisService(
            Catalog,
            ItemReferenceCatalogFactory.CreateEmbeddedProductionResolver(),
            catalog).Analyze(ParsePowers(scirocco));
        Assert.Equal("Smoke", Assert.Single(named.Sets).PowerName);

        var fallback = CreateService().Analyze(ParsePowers(scirocco));
        Assert.Equal("Power 0", Assert.Single(fallback.Sets).PowerName);
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

    private sealed class FakePowerCatalog(IEnumerable<HomecomingPowerReference> powers)
        : IHomecomingPowerReferenceCatalog
    {
        private readonly Dictionary<string, HomecomingPowerReference> _powers = powers.ToDictionary(
            power => $"{power.CategoryId}.{power.PowersetId}.{power.PowerId}",
            StringComparer.OrdinalIgnoreCase);

        public bool IsLoaded => true;

        public bool TryResolve(
            string categoryId,
            string powersetId,
            string powerId,
            out HomecomingPowerReference power) =>
            _powers.TryGetValue($"{categoryId}.{powersetId}.{powerId}", out power);
    }

    private static IEnumerable<string> SetTokens(string setId, int count) =>
        Catalog.GetEnhancements(ReferenceCatalogQueryScope.CurrentHomecoming)
            .Where(item => item.EnhancementSetId == setId)
            .Take(count)
            .Select(item => SourceToken(item.SourceVariants.First().HomecomingSourceId));

    private static HomecomingBuildLayoutSnapshot ParseTokens(IEnumerable<string> tokens) =>
        Parse(string.Join(Environment.NewLine, tokens.Select(token => $"    {token} (50)")));

    private static string GlobalPieceToken(string catalogItemId)
    {
        Assert.True(Catalog.TryGetById(catalogItemId, out var piece));
        return SourceToken(piece.SourceVariants.First().HomecomingSourceId);
    }

    private static HomecomingBuildLayoutSnapshot ParsePowers(params IEnumerable<string>[] powerTokenGroups)
    {
        var builder = new StringBuilder("Beta Hero: Level 50 Magic Class_Brute\n");
        var index = 0;
        foreach (var group in powerTokenGroups)
        {
            builder.Append($"Level 1: Pool Fighting Power_{index}\n");
            foreach (var token in group)
            {
                builder.Append($"    {token} (50)\n");
            }

            index++;
        }

        Assert.True(HomecomingBuildLayoutParser.TryParse(builder.ToString(), out var snapshot));
        return snapshot;
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

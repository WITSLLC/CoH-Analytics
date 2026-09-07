using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

public sealed class EnhancementHelpResolverTests
{
    private static EnhancementHelpResolverProductionInputs LoadInputs() =>
        ItemReferenceCatalogFactory.LoadEmbeddedProductionResolverInputs();

    private static IEnhancementHelpResolver CreateResolver(
        IReadOnlyDictionary<string, IReadOnlyList<float>>? namedTables = null)
    {
        var inputs = LoadInputs();
        Assert.True(inputs.Catalog.IsLoaded);
        Assert.NotNull(inputs.NamedTables);
        return new EnhancementHelpResolver(namedTables ?? inputs.NamedTables!);
    }

    private static EnhancementSourceVariantReferenceRecord RequireVariant(
        IItemReferenceCatalog catalog,
        string itemName,
        string homecomingSourceId)
    {
        Assert.True(catalog.TryResolve(itemName, out var resolution));
        return resolution.Item.SourceVariants.First(variant =>
            string.Equals(variant.HomecomingSourceId, homecomingSourceId, StringComparison.Ordinal));
    }

    [Fact]
    public void Production_Crafted_Accuracy_L50_resolves_to_observed_42_4_percent()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Invention: Accuracy",
            "Boosts.Crafted_Accuracy.Crafted_Accuracy");

        var result = resolver.Resolve(
            variant.DisplayHelp!,
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, result.Status);
        Assert.Equal("Increases accuracy by 42.4%.", result.ResolvedText);
    }

    [Fact]
    public void Production_Crafted_Positron_Acc_Dam_End_L26_resolves_to_observed_16_6_percent()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Positron's Blast: Accuracy/Damage/Endurance",
            "Boosts.Crafted_Positrons_Blast_E.Crafted_Positrons_Blast_E");

        var result = resolver.Resolve(
            variant.DisplayHelp!,
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 26 });

        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, result.Status);
        Assert.Equal(
            "Enhances the accuracy of a power by 16.6% and damage by 16.6% and reduces endurance cost by 16.6%.",
            result.ResolvedText);
    }

    [Fact]
    public void Production_observed_Homecoming_control_values_use_float32_percentage_arithmetic()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var accuracy = RequireVariant(
            inputs.Catalog,
            "Invention: Accuracy",
            "Boosts.Crafted_Accuracy.Crafted_Accuracy");
        var positronEnd = RequireVariant(
            inputs.Catalog,
            "Positron's Blast: Accuracy/Damage/Endurance",
            "Boosts.Crafted_Positrons_Blast_E.Crafted_Positrons_Blast_E");
        var positronAccDam = RequireVariant(
            inputs.Catalog,
            "Positron's Blast: Accuracy/Damage",
            "Boosts.Crafted_Positrons_Blast_A.Crafted_Positrons_Blast_A");

        Assert.Equal(
            "Increases accuracy by 25.6%.",
            resolver.Resolve(
                accuracy.DisplayHelp!,
                accuracy,
                new EnhancementHelpPresentationContext { PresentationLevel = 20 }).ResolvedText);
        Assert.Equal(
            "Increases accuracy by 42.4%.",
            resolver.Resolve(
                accuracy.DisplayHelp!,
                accuracy,
                new EnhancementHelpPresentationContext { PresentationLevel = 50 }).ResolvedText);
        Assert.Equal(
            "Enhances the accuracy of a power by 12.8% and damage by 12.8% and reduces endurance cost by 12.8%.",
            resolver.Resolve(
                positronEnd.DisplayHelp!,
                positronEnd,
                new EnhancementHelpPresentationContext { PresentationLevel = 20 }).ResolvedText);
        Assert.Equal(
            "Enhances the accuracy of a power by 16% and damage by 16% and reduces endurance cost by 16%.",
            resolver.Resolve(
                positronEnd.DisplayHelp!,
                positronEnd,
                new EnhancementHelpPresentationContext { PresentationLevel = 25 }).ResolvedText);
        Assert.Equal(
            "Enhances the damage of a power by 16% and accuracy by 16%.",
            resolver.Resolve(
                positronAccDam.DisplayHelp!,
                positronAccDam,
                new EnhancementHelpPresentationContext { PresentationLevel = 20 }).ResolvedText);
        Assert.Equal(
            "Enhances the damage of a power by 20% and accuracy by 20%.",
            resolver.Resolve(
                positronAccDam.DisplayHelp!,
                positronAccDam,
                new EnhancementHelpPresentationContext { PresentationLevel = 25 }).ResolvedText);
    }

    [Fact]
    public void Level_50_uses_table_index_49_not_50()
    {
        var inputs = LoadInputs();
        var table = inputs.NamedTables!["Melee_Boosts_33"];
        var variant = RequireVariant(
            inputs.Catalog,
            "Invention: Accuracy",
            "Boosts.Crafted_Accuracy.Crafted_Accuracy");

        var index49 = table[49] * variant.Effects[0].Scale * 100f;
        var index50 = table[50] * variant.Effects[0].Scale * 100f;
        Assert.NotEqual(index49, index50, 2);

        var resolver = CreateResolver(inputs.NamedTables);
        var result = resolver.Resolve(
            "{Boost.Attrib.Accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal(
            EnhancementHelpScaleFormatter.FormatDisplayPercentage(index49),
            result.ResolvedText);
        Assert.NotEqual(
            EnhancementHelpScaleFormatter.FormatDisplayPercentage(index50),
            result.ResolvedText);
    }

    [Fact]
    public void Formatter_rounds_one_decimal_AwayFromZero()
    {
        Assert.Equal("42.4", Format(42.38));
        Assert.Equal("44.5", Format(44.499));
        Assert.Equal("46.6", Format(46.618));
    }

    [Fact]
    public void Formatter_suppresses_trailing_zero_decimal()
    {
        Assert.Equal("53", Format(53.0));
        Assert.Equal("42", Format(42.0));
    }

    [Fact]
    public void Formatter_does_not_append_percent_sign()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Invention: Accuracy",
            "Boosts.Crafted_Accuracy.Crafted_Accuracy");

        var result = resolver.Resolve(
            "Value={Boost.Attrib.Accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.DoesNotContain("%", result.ResolvedText, StringComparison.Ordinal);
        Assert.StartsWith("Value=42.4", result.ResolvedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_Attuned_Positrons_Accuracy_Damage_L50_resolves_to_26_5_each()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Positron's Blast: Accuracy/Damage",
            "Boosts.Attuned_Positrons_Blast_A.Attuned_Positrons_Blast_A");

        var result = resolver.Resolve(
            variant.DisplayHelp!,
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, result.Status);
        Assert.Equal(
            "Enhances the damage of a power by 26.5% and accuracy by 26.5%.",
            result.ResolvedText);
    }

    [Fact]
    public void Attuned_MaxBoostLevel_clamps_presentation_level()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Bonesnap: Accuracy/Damage",
            "Boosts.Attuned_Bonesnap_A.Attuned_Bonesnap_A");
        var table = inputs.NamedTables!["Melee_Boosts_33"];

        var clampedRaw = table[24] * variant.Effects[0].Scale * 100.0;
        var unclampedRaw = table[49] * variant.Effects[0].Scale * 100.0;
        Assert.NotEqual(clampedRaw, unclampedRaw, 2);

        var at50 = resolver.Resolve(
            "{Boost.Attrib.Accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });
        var at25 = resolver.Resolve(
            "{Boost.Attrib.Accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 25 });

        Assert.Equal(at25.ResolvedText, at50.ResolvedText);
        Assert.Equal(Format(clampedRaw), at50.ResolvedText);
        Assert.NotEqual(Format(unclampedRaw), at50.ResolvedText);
    }

    [Fact]
    public void Static_context_has_no_combat_level_input()
    {
        var properties = typeof(EnhancementHelpPresentationContext).GetProperties();
        Assert.Single(properties);
        Assert.Equal("PresentationLevel", properties[0].Name);
    }

    [Fact]
    public void Crafted_lower_presentation_level_uses_different_table_lookup_than_L50()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Invention: Accuracy",
            "Boosts.Crafted_Accuracy.Crafted_Accuracy");

        var low = resolver.Resolve(
            "{Boost.Attrib.Accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 10 });
        var high = resolver.Resolve(
            "{Boost.Attrib.Accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.NotEqual(low.ResolvedText, high.ResolvedText);
        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, low.Status);
        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, high.Status);
    }

    [Fact]
    public void Training_Melee_Ones_representative_resolves_from_production_table()
    {
        var inputs = LoadInputs();
        var table = inputs.NamedTables!["Melee_Ones"];
        var variant = RequireVariant(
            inputs.Catalog,
            "Training: Accuracy",
            "Boosts.Generic_Accuracy.Generic_Accuracy");
        var expected = Format(table[49] * variant.Effects[0].Scale * 100.0);

        var resolver = CreateResolver(inputs.NamedTables);
        var result = resolver.Resolve(
            variant.DisplayHelp!,
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, result.Status);
        Assert.Contains(expected, result.ResolvedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Multi_token_template_resolves_each_supported_token_independently()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Positron's Blast: Accuracy/Damage",
            "Boosts.Attuned_Positrons_Blast_A.Attuned_Positrons_Blast_A");

        var result = resolver.Resolve(
            "Damage={Boost.Attrib.Damage.Scale}; Accuracy={Boost.Attrib.Accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, result.Status);
        Assert.Equal("Damage=26.5; Accuracy=26.5", result.ResolvedText);
    }

    [Fact]
    public void Tag_binding_is_case_insensitive()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Invention: Accuracy",
            "Boosts.Crafted_Accuracy.Crafted_Accuracy");

        var result = resolver.Resolve(
            "{Boost.Attrib.accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal("42.4", result.ResolvedText);
    }

    [Fact]
    public void Token_grammar_tolerates_internal_whitespace()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Invention: Defense Buff",
            "Boosts.Crafted_Defense_Buff.Crafted_Defense_Buff");

        var result = resolver.Resolve(
            "{ Boost.Attrib.Defense.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, result.Status);
        Assert.DoesNotContain("{ Boost.Attrib.Defense.Scale}", result.ResolvedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Regen_and_Regeneration_tags_remain_distinct()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var numina = RequireVariant(
            inputs.Catalog,
            "Numina's Convalescence: Regeneration/Recovery",
            "Boosts.Crafted_Numinas_Convalesence_F.Crafted_Numinas_Convalesence_F");
        Assert.True(inputs.Catalog.TryResolve("Regeneration Decrease", out var regenDecrease));
        var regeneration = regenDecrease.Item.SourceVariants.First(variant =>
            string.Equals(
                variant.HomecomingSourceId,
                "Boosts.Crafted_Decreased_Regeneration.Crafted_Decreased_Regeneration",
                StringComparison.Ordinal));

        var regenResult = resolver.Resolve(
            "{Boost.Attrib.Regen.Scale}",
            numina,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });
        var regenerationResult = resolver.Resolve(
            "{Boost.Attrib.Regeneration.Scale}",
            regeneration,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, regenResult.Status);
        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, regenerationResult.Status);
        Assert.NotEqual(regenResult.ResolvedText, regenerationResult.ResolvedText);
    }

    [Fact]
    public void Unsupported_token_family_is_preserved()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Invention: Accuracy",
            "Boosts.Crafted_Accuracy.Crafted_Accuracy");
        const string template = "Before {Future.Custom.Token} after {Boost.Attrib.Accuracy.Scale}.";

        var result = resolver.Resolve(
            template,
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, result.Status);
        Assert.Contains("{Future.Custom.Token}", result.ResolvedText, StringComparison.Ordinal);
        Assert.Contains("42.4", result.ResolvedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_tag_leaves_token_and_reports_diagnostic()
    {
        var resolver = CreateResolver();
        var variant = new EnhancementSourceVariantReferenceRecord
        {
            HomecomingSourceId = "Boosts.Test.Test",
            SourceForm = "Crafted",
            Effects = [],
        };

        var result = resolver.Resolve(
            "{Boost.Attrib.Accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal(EnhancementHelpResolutionStatus.PartiallyResolved, result.Status);
        Assert.Equal("{Boost.Attrib.Accuracy.Scale}", result.ResolvedText);
        Assert.Contains("{Boost.Attrib.Accuracy.Scale}", result.UnresolvedTokens);
    }

    [Fact]
    public void Conflicting_matching_effects_leave_token_and_report_diagnostic()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = new EnhancementSourceVariantReferenceRecord
        {
            HomecomingSourceId = "Boosts.Test.Conflict",
            SourceForm = "Crafted",
            Effects =
            [
                new EnhancementSourceVariantEffectReferenceRecord
                {
                    Tag = "Accuracy",
                    Table = "Melee_Boosts_33",
                    Scale = 1.0f,
                },
                new EnhancementSourceVariantEffectReferenceRecord
                {
                    Tag = "accuracy",
                    Table = "Melee_Boosts_33",
                    Scale = 0.5f,
                },
            ],
        };

        var result = resolver.Resolve(
            "{Boost.Attrib.Accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal(EnhancementHelpResolutionStatus.PartiallyResolved, result.Status);
        Assert.Equal("{Boost.Attrib.Accuracy.Scale}", result.ResolvedText);
        Assert.Contains(result.Diagnostics, value =>
            value.Contains("Conflicting Scale evidence", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_named_table_does_not_invent_value()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = new EnhancementSourceVariantReferenceRecord
        {
            HomecomingSourceId = "Boosts.Test.MissingTable",
            SourceForm = "Crafted",
            Effects =
            [
                new EnhancementSourceVariantEffectReferenceRecord
                {
                    Tag = "Accuracy",
                    Table = "Not_A_Promoted_Table",
                    Scale = 1.0f,
                },
            ],
        };

        var result = resolver.Resolve(
            "{Boost.Attrib.Accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal(EnhancementHelpResolutionStatus.PartiallyResolved, result.Status);
        Assert.Equal("{Boost.Attrib.Accuracy.Scale}", result.ResolvedText);
        Assert.Contains(result.Diagnostics, value =>
            value.Contains("Not_A_Promoted_Table", StringComparison.Ordinal));
    }

    [Fact]
    public void Logical_help_disagreement_uses_variant_specific_template()
    {
        var inputs = LoadInputs();
        Assert.True(inputs.Catalog.TryGetById("ENH-00804", out var item));
        Assert.Null(item.DisplayHelp);
        var crafted = item.SourceVariants.First(variant =>
            string.Equals(variant.SourceForm, "Crafted", StringComparison.Ordinal));
        var attuned = item.SourceVariants.First(variant =>
            string.Equals(variant.SourceForm, "Attuned", StringComparison.Ordinal));
        Assert.NotEqual(crafted.DisplayHelp, attuned.DisplayHelp);

        var resolver = CreateResolver(inputs.NamedTables);
        var craftedResult = resolver.Resolve(
            crafted.DisplayHelp!,
            crafted,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });
        var attunedResult = resolver.Resolve(
            attuned.DisplayHelp!,
            attuned,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, craftedResult.Status);
        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, attunedResult.Status);
        Assert.NotEqual(craftedResult.ResolvedText, attunedResult.ResolvedText);
    }

    [Fact]
    public void Steadfast_empty_tag_edge_remains_unresolved()
    {
        var inputs = LoadInputs();
        Assert.True(inputs.Catalog.TryGetEnhancementSetById("SET-00135", out var set));
        var power = set.Bonuses
            .SelectMany(bonus => bonus.AutoPowers)
            .First(autoPower => autoPower.HomecomingSourceId.Contains(
                "Steadfast_Protection_Def",
                StringComparison.Ordinal));
        var variant = new EnhancementSourceVariantReferenceRecord
        {
            HomecomingSourceId = power.HomecomingSourceId,
            SourceForm = "SetBonus",
            DisplayHelp = power.DisplayHelp,
            BoostUsePlayerLevel = power.BoostUsePlayerLevel,
            MaxBoostLevel = power.MaxBoostLevel,
            BoostBoostable = power.BoostBoostable,
            Effects = power.Effects,
        };

        var resolver = CreateResolver(inputs.NamedTables);
        var result = resolver.Resolve(
            variant.DisplayHelp!,
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal(EnhancementHelpResolutionStatus.PartiallyResolved, result.Status);
        Assert.Contains("{ Boost.Attrib.Defense.Scale}", result.ResolvedText, StringComparison.Ordinal);
        Assert.Contains("{ Boost.Attrib.Defense.Scale}", result.UnresolvedTokens);
    }

    [Fact]
    public void Repeated_resolution_is_deterministic()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Invention: Accuracy",
            "Boosts.Crafted_Accuracy.Crafted_Accuracy");
        var context = new EnhancementHelpPresentationContext { PresentationLevel = 50 };

        var first = resolver.Resolve(variant.DisplayHelp!, variant, context);
        var second = resolver.Resolve(variant.DisplayHelp!, variant, context);

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.ResolvedText, second.ResolvedText);
        Assert.Equal(first.UnresolvedTokens, second.UnresolvedTokens);
        Assert.Equal(first.Diagnostics, second.Diagnostics);
    }

    [Fact]
    public void Static_L50_Crafted_Accuracy_boundary_is_42_4_not_booster_curve()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Invention: Accuracy",
            "Boosts.Crafted_Accuracy.Crafted_Accuracy");

        var result = resolver.Resolve(
            "{Boost.Attrib.Accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal("42.4", result.ResolvedText);
        Assert.NotEqual("44.5", result.ResolvedText);
        Assert.NotEqual("53", result.ResolvedText);
    }

    [Fact]
    public void Invalid_presentation_level_returns_invalid_input_without_throwing()
    {
        var resolver = CreateResolver();
        var variant = new EnhancementSourceVariantReferenceRecord
        {
            HomecomingSourceId = "Boosts.Test.Test",
            SourceForm = "Crafted",
        };

        var result = resolver.Resolve(
            "{Boost.Attrib.Accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 0 });

        Assert.Equal(EnhancementHelpResolutionStatus.InvalidInput, result.Status);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Theory]
    [InlineData(50, "42.4")]
    [InlineData(51, "44.5")]
    [InlineData(52, "46.6")]
    [InlineData(53, "48.7")]
    [InlineData(54, "50.9")]
    [InlineData(55, "53")]
    public void Production_Crafted_Accuracy_boosted_presentation_levels_match_observed_values(
        int presentationLevel,
        string expectedAccuracy)
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Invention: Accuracy",
            "Boosts.Crafted_Accuracy.Crafted_Accuracy");

        var result = resolver.Resolve(
            "{Boost.Attrib.Accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = presentationLevel });

        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, result.Status);
        Assert.Equal(expectedAccuracy, result.ResolvedText);
    }

    [Fact]
    public void Production_Soulbound_Damage_Endurance_L55_resolves_both_aspects_to_41_4()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Soulbound Allegiance: Damage/Endurance Reduction",
            "Boosts.Crafted_Soulbound_Allegiance_E.Crafted_Soulbound_Allegiance_E");

        var result = resolver.Resolve(
            "Damage={Boost.Attrib.Damage.Scale}; End={Boost.Attrib.Endurance.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 55 });

        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, result.Status);
        Assert.Equal("Damage=41.4; End=41.4", result.ResolvedText);
    }

    [Fact]
    public void Production_Soulbound_Damage_Endurance_L50_remains_native_cap_value()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Soulbound Allegiance: Damage/Endurance Reduction",
            "Boosts.Crafted_Soulbound_Allegiance_E.Crafted_Soulbound_Allegiance_E");

        var result = resolver.Resolve(
            "Damage={Boost.Attrib.Damage.Scale}; End={Boost.Attrib.Endurance.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 50 });

        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, result.Status);
        Assert.Equal("Damage=33.1; End=33.1", result.ResolvedText);
    }

    [Fact]
    public void Attuned_variant_does_not_resolve_boosted_presentation_levels()
    {
        var inputs = LoadInputs();
        var resolver = CreateResolver(inputs.NamedTables);
        var variant = RequireVariant(
            inputs.Catalog,
            "Positron's Blast: Accuracy/Damage",
            "Boosts.Attuned_Positrons_Blast_A.Attuned_Positrons_Blast_A");

        Assert.False(EnhancementHelpResolverBoosterMultipliers.SupportsBoostedPresentationLevels(variant));

        var result = resolver.Resolve(
            "{Boost.Attrib.Accuracy.Scale}",
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = 51 });

        Assert.Equal(EnhancementHelpResolutionStatus.PartiallyResolved, result.Status);
        Assert.Contains("{Boost.Attrib.Accuracy.Scale}", result.UnresolvedTokens);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Contains("boostable crafted", StringComparison.OrdinalIgnoreCase));
    }

    private static string Format(double rawPercentage) =>
        EnhancementHelpScaleFormatter.FormatDisplayPercentage(rawPercentage);
}

using System.Text.RegularExpressions;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

/// <summary>
/// Research-only census of parameterized tokens in promoted Enhancement help templates.
/// Does not resolve tokens to player-facing percentages.
/// </summary>
public sealed class EnhancementHelpTokenCensusTests
{
    private static readonly Regex BraceTokenRegex = new(@"\{[^{}]+\}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [Fact]
    public void Current_DisplayHelp_token_census_matches_expected_grammar()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.IsLoaded, catalog.LoadFailureReason);

        var currentEnhancements = catalog.GetEnhancements(ReferenceCatalogQueryScope.CurrentHomecoming);
        Assert.Equal(1628, currentEnhancements.Count);

        var displayHelpTexts = CollectDisplayHelpTexts(currentEnhancements).ToArray();
        var withTokens = displayHelpTexts.Where(text => BraceTokenRegex.IsMatch(text)).ToArray();
        var withoutTokens = displayHelpTexts.Where(text => !BraceTokenRegex.IsMatch(text)).ToArray();

        Assert.Equal(4311, displayHelpTexts.Length);
        Assert.Equal(4099, withTokens.Length);
        Assert.Equal(212, withoutTokens.Length);

        var tokenCounts = CountTokens(withTokens);
        Assert.Equal(17, tokenCounts.Count);
        Assert.Equal(1660, tokenCounts["{Boost.Attrib.RechargeTime.Scale}"]);
        Assert.Equal(1335, tokenCounts["{Boost.Attrib.Accuracy.Scale}"]);
        Assert.Equal(11, tokenCounts["{Boost.Attrib.endurance.Scale}"]);
        Assert.Equal(3, tokenCounts["{Boost.Attrib.accuracy.Scale}"]);
        Assert.Equal(3, tokenCounts["{Boost.Attrib.Regen.Scale}"]);
        Assert.Equal(10, tokenCounts["{Boost.Attrib.Regeneration.Scale}"]);

        Assert.All(
            tokenCounts.Keys,
            token => Assert.Matches(@"^\{Boost\.Attrib\.[A-Za-z]+\.Scale\}$", token));

        Assert.Contains(withTokens, text => BraceTokenRegex.Matches(text).Count >= 2);
    }

    [Fact]
    public void Current_ShortHelp_contains_no_brace_tokens()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var currentEnhancements = catalog.GetEnhancements(ReferenceCatalogQueryScope.CurrentHomecoming);

        var shortHelpTexts = CollectShortHelpTexts(currentEnhancements).ToArray();
        Assert.NotEmpty(shortHelpTexts);
        Assert.All(shortHelpTexts, text => Assert.DoesNotMatch(BraceTokenRegex, text));
    }

    [Fact]
    public void Common_invention_and_origin_families_use_tokens_while_proc_text_may_not()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var currentEnhancements = catalog.GetEnhancements(ReferenceCatalogQueryScope.CurrentHomecoming);

        Assert.True(catalog.TryResolve("Invention: Accuracy", out var accuracy));
        Assert.Contains("{Boost.Attrib.Accuracy.Scale}", accuracy.Item.DisplayHelp, StringComparison.Ordinal);

        Assert.True(catalog.TryResolve("Eradication: Chance for Energy Damage", out var proc));
        Assert.NotEmpty(proc.Item.SourceVariants);
        var procHelps = proc.Item.SourceVariants
            .Select(variant => variant.DisplayHelp)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Cast<string>()
            .ToArray();
        Assert.NotEmpty(procHelps);
        Assert.All(procHelps, text => Assert.DoesNotMatch(BraceTokenRegex, text));

        var craftedInventionWithTokens = currentEnhancements
            .Where(item => item.EnhancementFamily == ReferenceEnhancementFamily.CraftedInvention)
            .SelectMany(item => item.SourceVariants)
            .Select(variant => variant.DisplayHelp)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        Assert.Equal(27, currentEnhancements.Count(item =>
            item.EnhancementFamily == ReferenceEnhancementFamily.CraftedInvention));
        Assert.NotEmpty(craftedInventionWithTokens);
        Assert.All(craftedInventionWithTokens, text => Assert.Matches(BraceTokenRegex, text!));
    }

    [Fact]
    public void Set_bonus_auto_power_help_rarely_contains_tokens()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var sets = catalog.GetEnhancementSets(ReferenceCatalogQueryScope.CurrentHomecoming);
        Assert.Equal(227, sets.Count);

        var bonusHelps = sets.Values
            .SelectMany(set => set.Bonuses)
            .SelectMany(bonus => bonus.AutoPowers)
            .Select(power => power.DisplayHelp)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Cast<string>()
            .ToArray();

        Assert.Equal(1138, bonusHelps.Length);
        var withTokens = bonusHelps.Where(text => BraceTokenRegex.IsMatch(text)).ToArray();
        Assert.Equal(7, withTokens.Length);
        Assert.Contains(withTokens, text => text.Contains("{ Boost.Attrib.Defense.Scale}", StringComparison.Ordinal));
    }

    [Fact]
    public void Stored_templates_remain_faithful_message_store_strings_not_pre_resolved()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Invention: Damage", out var damage));
        Assert.Equal(
            "Increases damage by {Boost.Attrib.Damage.Scale}%.",
            damage.Item.DisplayHelp);
        Assert.Equal("Enhances Damage", damage.Item.ShortHelp);

        Assert.True(catalog.TryResolve("Bonesnap: Accuracy/Damage", out var bonesnap));
        Assert.Contains("{Boost.Attrib.Damage.Scale}", bonesnap.Item.DisplayHelp, StringComparison.Ordinal);
        Assert.Contains("{Boost.Attrib.Accuracy.Scale}", bonesnap.Item.DisplayHelp, StringComparison.Ordinal);
        Assert.DoesNotContain("33%", bonesnap.Item.DisplayHelp, StringComparison.Ordinal);
        Assert.DoesNotContain("42.4%", bonesnap.Item.DisplayHelp, StringComparison.Ordinal);
    }

    private static IEnumerable<string> CollectDisplayHelpTexts(IEnumerable<ItemReferenceRecord> enhancements)
    {
        foreach (var item in enhancements)
        {
            if (!string.IsNullOrWhiteSpace(item.DisplayHelp))
            {
                yield return item.DisplayHelp;
            }

            foreach (var variant in item.SourceVariants)
            {
                if (!string.IsNullOrWhiteSpace(variant.DisplayHelp))
                {
                    yield return variant.DisplayHelp;
                }
            }
        }
    }

    private static IEnumerable<string> CollectShortHelpTexts(IEnumerable<ItemReferenceRecord> enhancements)
    {
        foreach (var item in enhancements)
        {
            if (!string.IsNullOrWhiteSpace(item.ShortHelp))
            {
                yield return item.ShortHelp;
            }

            foreach (var variant in item.SourceVariants)
            {
                if (!string.IsNullOrWhiteSpace(variant.ShortHelp))
                {
                    yield return variant.ShortHelp;
                }
            }
        }
    }

    private static Dictionary<string, int> CountTokens(IEnumerable<string> texts)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            foreach (Match match in BraceTokenRegex.Matches(text))
            {
                counts.TryGetValue(match.Value, out var count);
                counts[match.Value] = count + 1;
            }
        }

        return counts;
    }
}

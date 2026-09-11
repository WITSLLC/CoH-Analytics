using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class HomecomingHelpDisplayFormatterTests
{
    [Theory]
    [InlineData("Line one<br>Line two")]
    [InlineData("Line one<br/>Line two")]
    [InlineData("Line one<br />Line two")]
    [InlineData("Line one<BR>Line two")]
    public void Br_tags_become_line_breaks(string input)
    {
        var actual = HomecomingHelpDisplayFormatter.NormalizeForDisplay(input);
        Assert.Equal($"Line one{Environment.NewLine}Line two", actual);
    }

    [Fact]
    public void Color_wrappers_preserve_text_and_remove_tags()
    {
        const string input = "Damage over time.<br><br><color #cfc95>Recharge: Very Fast</color>";
        var expected = $"Damage over time.{Environment.NewLine}{Environment.NewLine}Recharge: Very Fast";

        var actual = HomecomingHelpDisplayFormatter.NormalizeForDisplay(input);

        Assert.Equal(expected, actual);
        Assert.DoesNotContain("<br", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<color", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("</color", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Multiple_consecutive_breaks_remain_readable()
    {
        const string input = "First<br><br><br>Second";
        var actual = HomecomingHelpDisplayFormatter.NormalizeForDisplay(input)!;

        Assert.Equal($"First{Environment.NewLine}{Environment.NewLine}Second", actual);
        Assert.DoesNotContain("<br", actual, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plain_text_is_unchanged()
    {
        const string input = "Toggles your fiery defenses.";
        Assert.Same(input, HomecomingHelpDisplayFormatter.NormalizeForDisplay(input));
    }

    [Theory]
    [InlineData("<color #cfc95>Recharge: Very Fast")]
    [InlineData("Damage over time.</color>")]
    [InlineData("<color>Partial")]
    public void Malformed_color_markup_does_not_throw(string input)
    {
        var actual = HomecomingHelpDisplayFormatter.NormalizeForDisplay(input);
        Assert.False(string.IsNullOrEmpty(actual));
    }

    [Fact]
    public void Null_and_whitespace_inputs_pass_through()
    {
        Assert.Null(HomecomingHelpDisplayFormatter.NormalizeForDisplay(null));
        Assert.Equal(string.Empty, HomecomingHelpDisplayFormatter.NormalizeForDisplay(string.Empty));
        Assert.Equal("   ", HomecomingHelpDisplayFormatter.NormalizeForDisplay("   "));
    }
}

public sealed class AccountsBuildPowerTooltipMarkupTests
{
    [Fact]
    public void Build_power_tooltip_normalizes_homecoming_markup()
    {
        const string content = """
            Alpha Hero: Level 38 Magic Class_Brute
            Level 1: Brute_Melee Fiery_Melee Scorch
                EMPTY
            """;
        Assert.True(HomecomingBuildLayoutParser.TryParse(content, out var snapshot));

        var catalog = new FakePowerCatalog(
        [
            new HomecomingPowerReference(
                "Brute_Melee",
                "Fiery_Melee",
                "Scorch",
                "Fiery Melee",
                "Scorch",
                "Damage over time.<br><br><color #cfc95>Recharge: Very Fast</color>",
                null,
                false,
                false,
                HomecomingPowerType.Click)
        ]);

        var presentation = AccountsBuildPresentationSupport.Build(
            snapshot,
            "Fiery Melee",
            null,
            catalog,
            assetProvider: null,
            itemCatalog: null,
            enhancementIconCompositor: null,
            boostMetadataProvider: null);

        var tooltip = Assert.Single(presentation.PrimarySection!.Powers).Tooltip;
        Assert.Contains("Damage over time.", tooltip, StringComparison.Ordinal);
        Assert.Contains("Recharge: Very Fast", tooltip, StringComparison.Ordinal);
        Assert.DoesNotContain("<br", tooltip, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<color", tooltip, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("</color", tooltip, StringComparison.OrdinalIgnoreCase);
    }

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
}

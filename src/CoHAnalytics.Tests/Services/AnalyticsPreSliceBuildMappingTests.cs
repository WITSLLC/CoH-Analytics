using System.Text.Json;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

/// <summary>Repository evidence for a future resolver; does not implement proc attribution.</summary>
public sealed class AnalyticsPreSliceBuildMappingTests
{
    private static readonly Lazy<JsonDocument> Catalog = new(() => JsonDocument.Parse(File.ReadAllText(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "CoHAnalytics", "ReferenceData", "item-catalog.v1.json")))));

    [Fact]
    public void Captured_layout_has_one_Crafted_Armageddon_F_slot_in_Hot_Feet_and_catalog_identity_matches()
    {
        var layout = Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Services", "Fixtures", "hells-vengence-build-layout.txt")));
        var matchingOccurrences = layout.Powers
            .SelectMany(power => power.Slots.Select(slot => (power, slot)))
            .Where(pair => pair.slot.RawEnhancementToken == "Crafted_Armageddon_F")
            .ToArray();
        var occurrence = Assert.Single(matchingOccurrences);
        Assert.Equal("Hot_Feet", occurrence.power.RawPowerToken);
        Assert.Equal(10, occurrence.power.AcquisitionLevel);
        Assert.Equal(5, occurrence.slot.SlotOrder);
        Assert.False(occurrence.slot.IsAttuned);

        var item = CatalogItem("ENH-01287");
        Assert.Equal("Armageddon: Chance for Fire Damage", item.GetProperty("currentDisplayName").GetString());
        Assert.Equal("SetIO", item.GetProperty("subtype").GetString());
        Assert.Equal("VerifiedMultiSource", item.GetProperty("verificationStatus").GetString());
        Assert.Contains("Boosts.Crafted_Armageddon_F.Crafted_Armageddon_F", SourceIds(item));
        var loggedProcLine = File.ReadLines(Path.Combine(AppContext.BaseDirectory,
                "Fixtures", "Combat", "max-channel-2026-09-12.tsv"))
            .Single(line => line.StartsWith("633\t", StringComparison.Ordinal))
            .Split('\t', 2)[1];
        Assert.True(CombatEventParserTestSupport.TryParseLine(loggedProcLine, out var loggedProc));
        Assert.Equal(CombatGrammarId.Dmg01YouHitWithPower, loggedProc.GrammarId);
        Assert.Equal(item.GetProperty("currentDisplayName").GetString(), loggedProc.PowerName);
    }

    [Fact]
    public void Duplicate_slot_occurrences_in_one_power_fail_unique_occurrence_precondition()
    {
        var layout = Parse("""
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Armageddon_F (50)
                Crafted_Armageddon_F (50)
            """);
        var occurrences = layout.Powers
            .SelectMany(power => power.Slots.Select(slot => (power.RawPowerToken, slot.SlotOrder, slot.RawEnhancementToken)))
            .Where(value => value.RawEnhancementToken == "Crafted_Armageddon_F")
            .ToArray();
        Assert.Equal(2, occurrences.Length);
        Assert.Equal([0, 1], occurrences.Select(value => value.SlotOrder));
        Assert.Single(occurrences.Select(value => value.RawPowerToken).Distinct());
        Assert.NotEqual(1, occurrences.Length); // Not BuildConfirmed, despite one distinct parent.
    }

    [Fact]
    public void Unknown_token_has_no_catalog_source_variant_and_cannot_be_BuildConfirmed()
    {
        const string unknown = "Crafted_Uncatalogued_Proc_Z";
        var layout = Parse($"Level 10: Controller_Control Fire_Control Hot_Feet\n    {unknown} (50)");
        Assert.Equal(unknown, Assert.Single(Assert.Single(layout.Powers).Slots).RawEnhancementToken);
        Assert.DoesNotContain(AllSourceIds(), id => id == $"Boosts.{unknown}.{unknown}");
    }

    [Fact]
    public void Superior_attuned_variant_is_same_catalog_item_but_not_same_raw_token()
    {
        var layout = Parse("""
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Armageddon_F (50)
                Superior_Attuned_Armageddon_F (1)
            """);
        var slots = Assert.Single(layout.Powers).Slots;
        Assert.Equal("Crafted_Armageddon_F", slots[0].RawEnhancementToken);
        Assert.False(slots[0].IsAttuned);
        Assert.Equal("Superior_Attuned_Armageddon_F", slots[1].RawEnhancementToken);
        Assert.True(slots[1].IsAttuned);

        var item = CatalogItem("ENH-01287");
        Assert.Equal(
            ["Boosts.Crafted_Armageddon_F.Crafted_Armageddon_F",
             "Boosts.Superior_Attuned_Armageddon_F.Superior_Attuned_Armageddon_F"],
            SourceIds(item));
        Assert.DoesNotContain(AllSourceIds(), id => id == "Boosts.Attuned_Armageddon_F.Attuned_Armageddon_F");
        // This proves a shared catalog item, not that arbitrary attuned tokens are interchangeable.
    }

    [Fact]
    public void Reactive_Interface_has_no_current_exact_proc_parent_mapping()
    {
        Assert.DoesNotContain(Catalog.Value.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.TryGetProperty("currentDisplayName", out var name)
            && name.GetString() == "Reactive Interface");
        Assert.DoesNotContain(typeof(HomecomingBuildSlotSnapshot).GetProperties(), property =>
            property.Name.Contains("ProcIdentity", StringComparison.Ordinal)
            || property.Name.Contains("Global", StringComparison.Ordinal)
            || property.Name.Contains("Incarnate", StringComparison.Ordinal));
        // A separate global/Incarnate attribution path is required; raw slot presence alone is not proof.
    }

    private static HomecomingBuildLayoutSnapshot Parse(string text)
    {
        Assert.True(HomecomingBuildLayoutParser.TryParse(text, out var layout));
        return layout;
    }

    private static JsonElement CatalogItem(string id) =>
        Catalog.Value.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("catalogItemId").GetString() == id);

    private static string[] SourceIds(JsonElement item) =>
        item.GetProperty("sourceVariants").EnumerateArray()
            .Select(variant => variant.GetProperty("homecomingSourceId").GetString()!)
            .ToArray();

    private static IEnumerable<string> AllSourceIds() =>
        Catalog.Value.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.TryGetProperty("sourceVariants", out _))
            .SelectMany(item => SourceIds(item));
}

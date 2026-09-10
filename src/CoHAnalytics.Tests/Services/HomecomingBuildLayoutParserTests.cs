using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class HomecomingBuildLayoutParserTests
{
    [Fact]
    public void Primary_power_preserves_header_tokens_and_multiple_crafted_enhancements()
    {
        const string build = """
            Cinder Thread: Level 50 Magic Class_Controller
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Accuracy (50+5)
                Crafted_Eradication_F (30)
                Crafted_Sciroccos_Dervish_F (50)
            """;

        Assert.True(HomecomingBuildLayoutParser.TryParse(build, out var snapshot));

        Assert.Equal("Cinder Thread", snapshot.CharacterName);
        Assert.Equal(50, snapshot.CharacterLevel);
        Assert.Equal("Class_Controller", snapshot.RawClassToken);
        var power = Assert.Single(snapshot.Powers);
        Assert.Equal(10, power.AcquisitionLevel);
        Assert.Equal("Controller_Control", power.RawCategoryToken);
        Assert.Equal("Fire_Control", power.RawPowerSetToken);
        Assert.Equal("Hot_Feet", power.RawPowerToken);
        Assert.Equal(0, power.SourceOrder);
        Assert.Equal(
            ["Crafted_Accuracy", "Crafted_Eradication_F", "Crafted_Sciroccos_Dervish_F"],
            power.Slots.Select(slot => slot.RawEnhancementToken));
        Assert.Equal([0, 1, 2], power.Slots.Select(slot => slot.SlotOrder));
    }

    [Fact]
    public void Attuned_enhancement_preserves_raw_token_and_flag()
    {
        const string build = """
            Level 1: Controller_Control Fire_Control Soot
                Attuned_Cupids_Crush_D (1)
                Attuned_Cupids_Crush_E (1)
            """;

        Assert.True(HomecomingBuildLayoutParser.TryParse(build, out var snapshot));

        Assert.All(snapshot.Powers[0].Slots, slot => Assert.True(slot.IsAttuned));
        Assert.Equal("Attuned_Cupids_Crush_D", snapshot.Powers[0].Slots[0].RawEnhancementToken);
        Assert.Equal(1, snapshot.Powers[0].Slots[0].BaseEnhancementLevel);
        Assert.Null(snapshot.Powers[0].Slots[0].BoostValue);
    }

    [Fact]
    public void Boosted_enhancement_splits_base_level_and_boost_value()
    {
        const string build = """
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Accuracy (50+5)
            """;

        Assert.True(HomecomingBuildLayoutParser.TryParse(build, out var snapshot));

        var slot = Assert.Single(snapshot.Powers[0].Slots);
        Assert.Equal(50, slot.BaseEnhancementLevel);
        Assert.Equal(5, slot.BoostValue);
    }

    [Fact]
    public void Plain_enhancement_level_has_no_boost_value()
    {
        const string build = """
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Accuracy (25)
            """;

        Assert.True(HomecomingBuildLayoutParser.TryParse(build, out var snapshot));

        var slot = Assert.Single(snapshot.Powers[0].Slots);
        Assert.Equal(25, slot.BaseEnhancementLevel);
        Assert.Null(slot.BoostValue);
    }

    [Fact]
    public void Empty_slot_is_represented_explicitly()
    {
        const string build = """
            Level 6: Controller_Buff Kinetics Siphon_Power
                EMPTY
            """;

        Assert.True(HomecomingBuildLayoutParser.TryParse(build, out var snapshot));

        var slot = Assert.Single(snapshot.Powers[0].Slots);
        Assert.True(slot.IsEmpty);
        Assert.Null(slot.RawEnhancementToken);
        Assert.False(slot.IsAttuned);
        Assert.Null(slot.BaseEnhancementLevel);
        Assert.Null(slot.BoostValue);
        Assert.Equal(0, slot.SlotOrder);
    }

    [Fact]
    public void Power_with_no_slot_lines_has_an_empty_slot_collection()
    {
        const string build = """
            Level 1: Controller_Control Fire_Control Soot
            Level 2: Controller_Buff Kinetics Transfusion
                Crafted_Accuracy (25)
            """;

        Assert.True(HomecomingBuildLayoutParser.TryParse(build, out var snapshot));

        Assert.Empty(snapshot.Powers[0].Slots);
        Assert.Single(snapshot.Powers[1].Slots);
    }

    [Fact]
    public void Level_zero_power_is_preserved()
    {
        const string build = """
            Level 0: Pool Leaping Double_Jump
            """;

        Assert.True(HomecomingBuildLayoutParser.TryParse(build, out var snapshot));

        var power = Assert.Single(snapshot.Powers);
        Assert.Equal(0, power.AcquisitionLevel);
        Assert.Equal("Pool", power.RawCategoryToken);
        Assert.Equal("Leaping", power.RawPowerSetToken);
        Assert.Equal("Double_Jump", power.RawPowerToken);
    }

    [Fact]
    public void Duplicate_enhancement_tokens_preserve_occurrence_order()
    {
        const string build = """
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Accuracy (25)
                Crafted_Accuracy (30)
                Crafted_Accuracy (50+1)
            """;

        Assert.True(HomecomingBuildLayoutParser.TryParse(build, out var snapshot));

        Assert.Equal(3, snapshot.Powers[0].Slots.Count);
        Assert.All(
            snapshot.Powers[0].Slots,
            slot => Assert.Equal("Crafted_Accuracy", slot.RawEnhancementToken));
        Assert.Equal([25, 30, 50], snapshot.Powers[0].Slots.Select(slot => slot.BaseEnhancementLevel));
        Assert.Equal([0, 1, 2], snapshot.Powers[0].Slots.Select(slot => slot.SlotOrder));
    }

    [Fact]
    public void Realistic_level_fifty_layout_preserves_file_order_across_all_section_categories()
    {
        const string build = """
            Cinder Thread: Level 50 Magic Class_Controller
            Level 1: Inherent Inherent Brawl
                EMPTY
            Level 2: Inherent Fitness Health
                Crafted_Common_Heal (50)
            Level 1: Controller_Control Fire_Control Soot
                Attuned_Cupids_Crush_D (1)
            Level 1: Controller_Buff Kinetics Transfusion
                Crafted_Accuracy (50+5)
            Level 0: Pool Leaping Double_Jump
            Level 6: Pool Leaping Combat_Jumping
                Crafted_Defense_Buff (50)
            Level 35: Epic Fire_Mastery Fire_Ball
                Crafted_Positrons_Blast_D (50)
            ------------------
            Badges Earned:
            ------------------
            Level 49: Epic Ice_Mastery Hibernate
                Crafted_Recharge (50)
            """;

        Assert.True(HomecomingBuildLayoutParser.TryParse(build, out var snapshot));

        Assert.Equal(7, snapshot.Powers.Count);
        Assert.Equal(
            ["Inherent", "Inherent", "Controller_Control", "Controller_Buff", "Pool", "Pool", "Epic"],
            snapshot.Powers.Select(power => power.RawCategoryToken));
        Assert.Equal(
            ["Brawl", "Health", "Soot", "Transfusion", "Double_Jump", "Combat_Jumping", "Fire_Ball"],
            snapshot.Powers.Select(power => power.RawPowerToken));
        Assert.Equal(Enumerable.Range(0, 7), snapshot.Powers.Select(power => power.SourceOrder));
        Assert.DoesNotContain(snapshot.Powers, power => power.RawPowerToken == "Hibernate");
    }

    [Theory]
    [InlineData("not a buildsave")]
    [InlineData("Level 1: Controller_Control Fire_Control")]
    [InlineData("Level 1: Controller_Control Fire_Control Soot\n    Crafted_Accuracy (50+bogus)")]
    public void Malformed_input_fails_cleanly(string content)
    {
        Assert.False(HomecomingBuildLayoutParser.TryParse(content, out var snapshot));
        Assert.Null(snapshot);
    }

    [Theory]
    [InlineData("|MBD;12345;")]
    [InlineData("|MxDz;67890;")]
    [InlineData("Hero: Level 50 Magic Class_Controller\nLevel 1: Controller_Control Fire_Control Soot\n|MBD;12345;")]
    public void Compressed_buildsave_is_rejected(string content)
    {
        Assert.False(HomecomingBuildLayoutParser.TryParse(content, out var snapshot));
        Assert.Null(snapshot);
    }

    [Fact]
    public void Snapshot_collections_are_read_only()
    {
        const string build = """
            Level 1: Controller_Control Fire_Control Soot
                Crafted_Accuracy (25)
            """;

        Assert.True(HomecomingBuildLayoutParser.TryParse(build, out var snapshot));

        Assert.True(((IList<HomecomingBuildPowerSnapshot>)snapshot.Powers).IsReadOnly);
        Assert.True(((IList<HomecomingBuildSlotSnapshot>)snapshot.Powers[0].Slots).IsReadOnly);
    }
}

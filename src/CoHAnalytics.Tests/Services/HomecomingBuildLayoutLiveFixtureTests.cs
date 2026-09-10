using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class HomecomingBuildLayoutLiveFixtureTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory,
        "Services",
        "Fixtures",
        "hells-vengence-build-layout.txt");

    [Fact]
    public void Hells_vengence_level_fifty_layout_preserves_real_source_structure()
    {
        var content = File.ReadAllText(FixturePath);

        Assert.True(HomecomingBuildLayoutParser.TryParse(content, out var snapshot));
        Assert.Equal("Hell's Vengence", snapshot.CharacterName);
        Assert.Equal(50, snapshot.CharacterLevel);
        Assert.Equal("Class_Controller", snapshot.RawClassToken);
        Assert.Equal(37, snapshot.Powers.Count);
        Assert.Equal(103, snapshot.Powers.Sum(power => power.Slots.Count));
        Assert.Equal(93, snapshot.Powers.Sum(power => power.Slots.Count(slot => !slot.IsEmpty)));
        Assert.Equal(10, snapshot.Powers.Sum(power => power.Slots.Count(slot => slot.IsEmpty)));
        Assert.Equal(
            Enumerable.Range(0, snapshot.Powers.Count),
            snapshot.Powers.Select(power => power.SourceOrder));

        var hotFeet = FindPower(snapshot, "Hot_Feet");
        Assert.Equal(10, hotFeet.AcquisitionLevel);
        Assert.Equal("Controller_Control", hotFeet.RawCategoryToken);
        Assert.Equal("Fire_Control", hotFeet.RawPowerSetToken);
        Assert.Equal(
            [
                "Crafted_Accuracy",
                "Crafted_Eradication_F",
                "Crafted_Sciroccos_Dervish_F",
                "Crafted_Obliteration_F",
                "Crafted_Fury_of_the_Gladiator_F",
                "Crafted_Armageddon_F"
            ],
            hotFeet.Slots.Select(slot => slot.RawEnhancementToken));
        Assert.Equal(50, hotFeet.Slots[0].BaseEnhancementLevel);
        Assert.Equal(5, hotFeet.Slots[0].BoostValue);

        var siphonPowerSlot = Assert.Single(FindPower(snapshot, "Siphon_Power").Slots);
        Assert.True(siphonPowerSlot.IsEmpty);
        Assert.Null(siphonPowerSlot.RawEnhancementToken);

        var doubleJump = FindPower(snapshot, "Double_Jump");
        Assert.Equal(0, doubleJump.AcquisitionLevel);
        Assert.Empty(doubleJump.Slots);

        var kineticTransfer = FindPower(snapshot, "Kinetic_Transfer");
        Assert.Equal(
            ["Crafted_Recharge", "Crafted_Recharge"],
            kineticTransfer.Slots.Select(slot => slot.RawEnhancementToken));
        Assert.Equal([0, 1], kineticTransfer.Slots.Select(slot => slot.SlotOrder));

        var soot = FindPower(snapshot, "Soot");
        Assert.Equal(
            ["Attuned_Cupids_Crush_D", "Attuned_Cupids_Crush_E"],
            soot.Slots.Select(slot => slot.RawEnhancementToken));
        Assert.All(soot.Slots, slot => Assert.True(slot.IsAttuned));

        var superiorAttuned = Assert.Single(
            FindPower(snapshot, "Flashfire").Slots,
            slot => slot.RawEnhancementToken == "Superior_Attuned_Absolute_Amazement_F");
        Assert.True(superiorAttuned.IsAttuned);

        var luckOfTheGambler = Assert.Single(
            FindPower(snapshot, "Combat_Jumping").Slots,
            slot => slot.RawEnhancementToken == "Crafted_Luck_of_the_Gambler_A");
        Assert.Equal(50, luckOfTheGambler.BaseEnhancementLevel);
        Assert.Equal(5, luckOfTheGambler.BoostValue);

        Assert.Equal(
            ["Inherent", "Inherent Fitness", "Leaping", "Speed", "Fighting", "Fire_Mastery", "Leadership"],
            GetThirdColumnFirstAppearances(snapshot));
        Assert.True(
            FindPower(snapshot, "Fire_Shield").SourceOrder
            < FindPower(snapshot, "Defense").SourceOrder);

        Assert.Contains("Badges Earned:", content, StringComparison.Ordinal);
        Assert.Contains("ReclusesVictoryTour6", content, StringComparison.Ordinal);
        Assert.DoesNotContain(snapshot.Powers, power => power.RawPowerToken == "ReclusesVictoryTour6");
        Assert.DoesNotContain(
            snapshot.Powers.SelectMany(power => power.Slots),
            slot => slot.RawEnhancementToken == "ReclusesVictoryTour6");
    }

    private static HomecomingBuildPowerSnapshot FindPower(
        HomecomingBuildLayoutSnapshot snapshot,
        string rawPowerToken) =>
        Assert.Single(snapshot.Powers, power => power.RawPowerToken == rawPowerToken);

    private static IReadOnlyList<string> GetThirdColumnFirstAppearances(
        HomecomingBuildLayoutSnapshot snapshot)
    {
        var result = new List<string>();
        foreach (var power in snapshot.Powers)
        {
            var section = power.RawCategoryToken switch
            {
                "Inherent" when power.RawPowerSetToken == "Fitness" => "Inherent Fitness",
                "Inherent" when power.RawPowerSetToken == "Inherent" => "Inherent",
                "Pool" or "Epic" => power.RawPowerSetToken,
                _ => null
            };

            if (section is not null && !result.Contains(section, StringComparer.Ordinal))
            {
                result.Add(section);
            }
        }

        return result;
    }
}

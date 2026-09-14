using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Golden grammar-table coverage. Captures are raw.
/// </summary>
public sealed class GrammarMatcherGoldenTests
{
    [Theory]
    [MemberData(nameof(GoldenCaseData))]
    public void TryMatch_returns_expected_grammar_and_raw_captures(
        string body,
        CombatGrammarId expectedGrammar,
        string expectedCaptures)
    {
        Assert.True(GrammarMatcher.TryMatch(body, out var match));
        Assert.Equal(expectedGrammar, match.GrammarId);
        foreach (var pair in expectedCaptures.Split(';'))
        {
            var separator = pair.IndexOf('=');
            Assert.Equal(pair[(separator + 1)..], match.Capture(pair[..separator]));
        }
    }

    [Fact]
    public void Golden_table_covers_every_current_CombatGrammarId()
    {
        var covered = GoldenCases.Select(row => row.GrammarId).ToArray();
        Assert.Equal(covered.Length, covered.Distinct().Count());
        Assert.Equal(Enum.GetValues<CombatGrammarId>().Order().ToArray(), covered.Order().ToArray());
    }

    [Fact]
    public void Damage_with_power_captures_dot_and_effect_suffixes_as_raw_text()
    {
        Assert.True(GrammarMatcher.TryMatch(
            "You hit Lusca with your Fire Cages for 10.36 points of Fire damage over time (CONTAINMENT).",
            out var match));
        Assert.Equal(CombatGrammarId.Dmg01YouHitWithPower, match.GrammarId);
        Assert.Equal(" over time", match.Capture("suffix"));
        Assert.Equal("CONTAINMENT", match.Capture("effect"));
    }

    [Fact]
    public void Companion_miss_summary_is_recognized_as_canonical_only_grammar()
    {
        const string body = "Fire Cages missed!";
        Assert.True(GrammarMatcher.IsCompanionMissSummary(body));
        Assert.True(GrammarMatcher.TryMatch(body, out var match));
        Assert.Equal(CombatGrammarId.Cmp01CompanionMissSummary, match.GrammarId);
        Assert.Equal("Fire Cages", match.Capture("power"));
    }

    [Fact]
    public void Self_heal_precedence_wins_over_generic_target_heal_shape()
    {
        Assert.True(GrammarMatcher.TryMatch(
            "You heal yourself for 15 hit points with Regeneration.",
            out var match));
        Assert.Equal(CombatGrammarId.Heal02YouHealYourself, match.GrammarId);
        Assert.Equal("Regeneration", match.Capture("power"));
    }

    [Fact]
    public void Critical_incoming_precedence_wins_over_ordinary_hits_you_with()
    {
        Assert.True(GrammarMatcher.TryMatch(
            "Crey Thorn Mook critically hits you with Bone Shard for 22 points of Lethal damage.",
            out var match));
        Assert.Equal(CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower, match.GrammarId);
    }

    [Fact]
    public void Pet_prefixed_body_rematches_inner_grammar_with_prefix_entity()
    {
        Assert.True(GrammarMatcher.TryMatch(
            "Imp:  You hit Training Dummy with your Fire Ball for 12 points of Fire damage.",
            out var match));
        Assert.Equal(CombatGrammarId.Dmg01YouHitWithPower, match.GrammarId);
        Assert.Equal("Imp", match.PrefixEntity);
        Assert.Equal("Training Dummy", match.Capture("target"));
        Assert.False(GrammarMatcher.IsCompanionMissSummary(
            "Imp:  You hit Training Dummy with your Fire Ball for 12 points of Fire damage."));
    }

    [Fact]
    public void You_take_environmental_line_does_not_match_any_current_grammar()
    {
        Assert.False(GrammarMatcher.TryMatch(
            "You take 12 points of Toxic damage from Poison Gas.",
            out _));
    }

    public static TheoryData<string, CombatGrammarId, string> GoldenCaseData
    {
        get
        {
            var data = new TheoryData<string, CombatGrammarId, string>();
            foreach (var row in GoldenCases)
            {
                data.Add(row.Body, row.GrammarId, row.Captures);
            }

            return data;
        }
    }

    private static readonly GoldenCase[] GoldenCases =
    [
        new("You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
            CombatGrammarId.Dmg01YouHitWithPower,
            "target=Lusca;power=Hot Feet;amount=13.88;type=Fire"),
        new("You hit Training Dummy for 12 points of Fire damage.",
            CombatGrammarId.Dmg02YouHitWithoutPower,
            "target=Training Dummy;amount=12;type=Fire"),
        new("Crey Thorn Mook hits you with Bone Shard for 22.15 points of Lethal damage.",
            CombatGrammarId.Dmg03SourceHitsYouWithPower,
            "source=Crey Thorn Mook;power=Bone Shard;amount=22.15;type=Lethal"),
        new("Fictional Target hits you for 15 points of lethal damage.",
            CombatGrammarId.Dmg04SourceHitsYouWithoutPower,
            "source=Fictional Target;amount=15;type=lethal"),
        new("Lieutenant Skull critically hits you with Sniper Rifle for 55.0 points of Lethal damage.",
            CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower,
            "source=Lieutenant Skull;power=Sniper Rifle;amount=55.0;type=Lethal"),
        new("You heal Example Ally for 78.50 hit points with Healing Aura.",
            CombatGrammarId.Heal01YouHealTarget,
            "target=Example Ally;amount=78.50;power=Healing Aura"),
        new("You heal yourself for 15.00 hit points with Regeneration.",
            CombatGrammarId.Heal02YouHealYourself,
            "amount=15.00;power=Regeneration"),
        new("Example Medic heals you for 42.25 hit points with Aid.",
            CombatGrammarId.Heal03SourceHealsYou,
            "source=Example Medic;amount=42.25;power=Aid"),
        new("You activate Fire Cages.",
            CombatGrammarId.Act01YouActivate,
            "power=Fire Cages"),
        new("You activated the Fire Cages power.",
            CombatGrammarId.Act02YouActivatedThePower,
            "power=Fire Cages"),
        new("You have defeated Lusca",
            CombatGrammarId.Def01YouHaveDefeated,
            "target=Lusca"),
        new("Psiche has defeated Prototype Oscillator",
            CombatGrammarId.Def02OtherPlayerDefeated,
            "source=Psiche;target=Prototype Oscillator"),
        new("HIT Rikti Pylon! Your Flashfire power had a 95.00% chance to hit, you rolled a 51.51.",
            CombatGrammarId.Acc01RolledHit,
            "target=Rikti Pylon;power=Flashfire;chance=95.00;roll=51.51"),
        new("MISSED Spirit!! Your Fire Cages power had a 95.00% chance to hit, you rolled a 97.54.",
            CombatGrammarId.Acc02RolledMiss,
            "target=Spirit;power=Fire Cages;chance=95.00;roll=97.54"),
        new("HIT Lieutenant Skull! Your Fire Bolt power was forced to hit by streakbreaker.",
            CombatGrammarId.Acc03ForcedHit,
            "target=Lieutenant Skull;power=Fire Bolt"),
        new("HIT Training Dummy! Your Siphon Power power is autohit.",
            CombatGrammarId.Acc04Autohit,
            "target=Training Dummy;power=Siphon Power"),
        new("You heal Imp with Transfusion for 421.34 health points.",
            CombatGrammarId.Heal04YouHealTargetHealthPoints,
            "target=Imp;power=Transfusion;amount=421.34"),
        new("Hero_A heals you with their Panacea: Chance for +Hit Points/Endurance for 78.93 health points.",
            CombatGrammarId.Heal05SourceHealsYouWithTheirHealthPoints,
            "source=Hero_A;power=Panacea: Chance for +Hit Points/Endurance;amount=78.93"),
        new("Ally_B hits you with their Particle Burst for 31.64 points of Energy damage.",
            CombatGrammarId.Dmg06SourceHitsYouWithTheirPower,
            "source=Ally_B;power=Particle Burst;amount=31.64;type=Energy"),
        new("You hit Hero_A with your Panacea: Chance for +Hit Points/Endurance granting them 7.5 points of endurance.",
            CombatGrammarId.End01YouHitGrantingThemEndurance,
            "target=Hero_A;power=Panacea: Chance for +Hit Points/Endurance;amount=7.5"),
        new("Hero_A hits you with their Panacea: Chance for +Hit Points/Endurance granting you 7.5 points of endurance.",
            CombatGrammarId.End02SourceHitsYouGrantingYouEndurance,
            "source=Hero_A;power=Panacea: Chance for +Hit Points/Endurance;amount=7.5"),
        new("You Hold Sweeper with your Gravitational Anchor: Chance for Hold.",
            CombatGrammarId.Mez01YouStatusTargetWithPower,
            "status=Hold;target=Sweeper;power=Gravitational Anchor: Chance for Hold"),
        new("You knock Cleaner off their feet with your Ragnarok: Chance for Knockdown!",
            CombatGrammarId.Knk01YouKnockTargetOffFeet,
            "target=Cleaner;power=Ragnarok: Chance for Knockdown"),
        new("Fire Cages missed!",
            CombatGrammarId.Cmp01CompanionMissSummary,
            "power=Fire Cages"),
        new("Ally_B HITS you! Particle Burst power had a 57.64% chance to hit and rolled a 22.71.",
            CombatGrammarId.Acc05SourceHitsYouRolled,
            "source=Ally_B;power=Particle Burst;chance=57.64;roll=22.71"),
        new("Hero_A HITS you! Health power was autohit.",
            CombatGrammarId.Acc06SourceHitsYouAutohit,
            "source=Hero_A;power=Health")
    ];

    private sealed record GoldenCase(string Body, CombatGrammarId GrammarId, string Captures);
}

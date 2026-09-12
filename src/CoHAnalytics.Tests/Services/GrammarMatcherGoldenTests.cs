using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Golden grammar-table coverage for the Slice 1 split. Captures are raw; no new grammars.
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
    public void Companion_miss_summary_is_recognized_and_does_not_emit_a_grammar_hit()
    {
        const string body = "Fire Cages missed!";
        Assert.True(GrammarMatcher.IsCompanionMissSummary(body));
        Assert.False(GrammarMatcher.TryMatch(body, out var match));
        Assert.Null(match);
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
    public void Pet_prefixed_legacy_body_does_not_match()
    {
        Assert.False(GrammarMatcher.TryMatch(
            "Imp:  You hit Training Dummy with your Fire Ball for 12 points of Fire damage.",
            out _));
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
            "target=Training Dummy;power=Siphon Power")
    ];

    private sealed record GoldenCase(string Body, CombatGrammarId GrammarId, string Captures);
}

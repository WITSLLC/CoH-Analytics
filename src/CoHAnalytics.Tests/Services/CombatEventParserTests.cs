using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CombatEventParserTests
{
    [Fact]
    public void Damage_dealt_with_power_parses_scaled_amount_and_fields()
    {
        const string line = "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(CombatEventKind.DamageDealt, combatEvent.Kind);
        Assert.Equal(CombatGrammarId.Dmg01YouHitWithPower, combatEvent.GrammarId);
        Assert.Equal(CombatActorRole.Self, combatEvent.ActorRole);
        Assert.Equal("Lusca", combatEvent.TargetName);
        Assert.Equal("Hot Feet", combatEvent.PowerName);
        Assert.Equal(new CombatScaledAmount(1388), combatEvent.Amount);
        Assert.Equal("Fire", combatEvent.DamageType);
        Assert.False(combatEvent.IsOverTime);
        Assert.Null(combatEvent.EffectSuffix);
    }

    [Fact]
    public void Damage_dealt_dot_parses_over_time_flag()
    {
        const string line =
            "2026-08-04 12:00:01 You hit Lusca with your Fire Cages for 5.59 points of Fire damage over time.";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(CombatEventKind.DamageDealt, combatEvent.Kind);
        Assert.Equal(new CombatScaledAmount(559), combatEvent.Amount);
        Assert.True(combatEvent.IsOverTime);
        Assert.Null(combatEvent.EffectSuffix);
    }

    [Fact]
    public void Damage_dealt_containment_suffix_does_not_corrupt_amount_or_type()
    {
        const string line =
            "2026-08-04 12:00:02 You hit Lusca with your Fire Cages for 10.36 points of Fire damage over time (CONTAINMENT).";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(new CombatScaledAmount(1036), combatEvent.Amount);
        Assert.Equal("Fire", combatEvent.DamageType);
        Assert.True(combatEvent.IsOverTime);
        Assert.Equal("CONTAINMENT", combatEvent.EffectSuffix);
    }

    [Fact]
    public void Power_activation_you_activate_parses_power_name()
    {
        const string line = "2026-08-04 12:00:09 You activate Fire Cages.";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(CombatEventKind.PowerActivation, combatEvent.Kind);
        Assert.Equal(CombatGrammarId.Act01YouActivate, combatEvent.GrammarId);
        Assert.Equal("Fire Cages", combatEvent.PowerName);
        Assert.Equal(CombatScaledAmount.Zero, combatEvent.Amount);
    }

    [Fact]
    public void Power_activation_you_activated_the_power_parses_power_name()
    {
        const string line = "2026-08-04 12:00:10 You activated the Fire Cages power.";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(CombatEventKind.PowerActivation, combatEvent.Kind);
        Assert.Equal(CombatGrammarId.Act02YouActivatedThePower, combatEvent.GrammarId);
        Assert.Equal("Fire Cages", combatEvent.PowerName);
    }

    [Fact]
    public void Damage_received_with_power_parses_source_and_amount()
    {
        const string line =
            "2026-08-04 12:00:04 Crey Thorn Mook hits you with Bone Shard for 22.15 points of Lethal damage.";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(CombatEventKind.DamageReceived, combatEvent.Kind);
        Assert.Equal(CombatGrammarId.Dmg03SourceHitsYouWithPower, combatEvent.GrammarId);
        Assert.Equal(CombatActorRole.Other, combatEvent.ActorRole);
        Assert.Equal("Crey Thorn Mook", combatEvent.SourceName);
        Assert.Equal("Bone Shard", combatEvent.PowerName);
        Assert.Equal(new CombatScaledAmount(2215), combatEvent.Amount);
        Assert.Equal("Lethal", combatEvent.DamageType);
    }

    [Fact]
    public void Damage_received_without_power_parses_simple_form()
    {
        const string line = "2026-08-04 12:00:15 Fictional Target hits you for 15 points of lethal damage.";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(CombatEventKind.DamageReceived, combatEvent.Kind);
        Assert.Equal(CombatGrammarId.Dmg04SourceHitsYouWithoutPower, combatEvent.GrammarId);
        Assert.Equal("Fictional Target", combatEvent.SourceName);
        Assert.Null(combatEvent.PowerName);
        Assert.Equal(new CombatScaledAmount(1500), combatEvent.Amount);
        Assert.Equal("Lethal", combatEvent.DamageType);
    }

    [Fact]
    public void Healing_dealt_parses_target_power_and_amount()
    {
        const string line =
            "2026-08-04 12:00:06 You heal Example Ally for 78.50 hit points with Healing Aura.";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(CombatEventKind.HealingDealt, combatEvent.Kind);
        Assert.Equal(CombatGrammarId.Heal01YouHealTarget, combatEvent.GrammarId);
        Assert.Equal("Example Ally", combatEvent.TargetName);
        Assert.Equal("Healing Aura", combatEvent.PowerName);
        Assert.Equal(new CombatScaledAmount(7850), combatEvent.Amount);
    }

    [Fact]
    public void Healing_received_parses_source_power_and_amount()
    {
        const string line =
            "2026-08-04 12:00:07 Example Medic heals you for 42.25 hit points with Aid.";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(CombatEventKind.HealingReceived, combatEvent.Kind);
        Assert.Equal(CombatGrammarId.Heal03SourceHealsYou, combatEvent.GrammarId);
        Assert.Equal("Example Medic", combatEvent.SourceName);
        Assert.Equal("Aid", combatEvent.PowerName);
        Assert.Equal(new CombatScaledAmount(4225), combatEvent.Amount);
    }

    [Fact]
    public void Defeat_parses_target_without_terminal_period()
    {
        const string line = "2026-08-04 12:00:11 You have defeated Lusca";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(CombatEventKind.Defeat, combatEvent.Kind);
        Assert.Equal(CombatGrammarId.Def01YouHaveDefeated, combatEvent.GrammarId);
        Assert.Equal(CombatActorRole.Self, combatEvent.ActorRole);
        Assert.Equal("Lusca", combatEvent.TargetName);
        Assert.Null(combatEvent.SourceName);
    }

    [Fact]
    public void Other_player_defeat_parses_source_and_target()
    {
        const string line = "2026-08-04 12:00:12 Psiche has defeated Prototype Oscillator";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(CombatEventKind.Defeat, combatEvent.Kind);
        Assert.Equal(CombatGrammarId.Def02OtherPlayerDefeated, combatEvent.GrammarId);
        Assert.Equal(CombatActorRole.Other, combatEvent.ActorRole);
        Assert.Equal("Psiche", combatEvent.SourceName);
        Assert.Equal("Prototype Oscillator", combatEvent.TargetName);
    }

    [Fact]
    public void Other_player_defeat_preserves_apostrophes_in_names()
    {
        const string line = "2026-08-04 12:00:13 O'Brien has defeated Prototype Oscillator";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal("O'Brien", combatEvent.SourceName);
        Assert.Equal("Prototype Oscillator", combatEvent.TargetName);
    }

    [Fact]
    public void Three_line_defeat_subset_produces_three_total_and_one_my_defeat()
    {
        var lines = new[]
        {
            "2026-08-04 12:00:00 You have defeated Sprocket",
            "2026-08-04 12:00:01 Psiche has defeated Prototype Oscillator",
            "2026-08-04 12:00:02 Trapperkeeper has defeated Prototype Oscillator"
        };

        var aggregator = new CombatAggregator();
        var sequence = 1L;
        foreach (var line in lines)
        {
            Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent, sequence));
            aggregator.Apply(combatEvent);
            sequence++;
        }

        var snapshot = aggregator.ToSnapshot(
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 4, 12, 0, 10, TimeSpan.Zero),
            null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(3, snapshot.TotalDefeated);
        Assert.Equal(1, snapshot.MyDefeats);
    }

    [Fact]
    public void Consecutive_defeat_burst_matches_real_world_pattern()
    {
        var lines = new[]
        {
            "2026-08-04 12:00:00 You have defeated Sprocket",
            "2026-08-04 12:00:01 Psiche has defeated Prototype Oscillator",
            "2026-08-04 12:00:02 You have defeated Sprocket",
            "2026-08-04 12:00:03 You have defeated Prototype Oscillator",
            "2026-08-04 12:00:04 Trapperkeeper has defeated Prototype Oscillator",
            "2026-08-04 12:00:05 Psiche has defeated Prototype Oscillator"
        };

        var aggregator = new CombatAggregator();
        var sequence = 1L;
        foreach (var line in lines)
        {
            Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent, sequence));
            aggregator.Apply(combatEvent);
            sequence++;
        }

        var snapshot = aggregator.ToSnapshot(
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 4, 12, 0, 10, TimeSpan.Zero),
            null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(6, snapshot.TotalDefeated);
        Assert.Equal(3, snapshot.MyDefeats);
    }

    [Fact]
    public void Unrelated_has_defeated_text_does_not_emit_combat_event()
    {
        const string line = "2026-08-04 12:00:14 The briefing has defeated morale among the team.";

        Assert.False(CombatEventParserTestSupport.TryParseLine(line, out _));
    }

    [Fact]
    public void Non_combat_lines_do_not_emit_combat_events()
    {
        const string xpLine = "2026-08-04 12:00:12 You gain 500 experience and 100 influence.";
        const string zoneLine = "2026-08-04 12:00:13 Entering the neighborhood.";

        Assert.False(CombatEventParserTestSupport.TryParseLine(xpLine, out _));
        Assert.False(CombatEventParserTestSupport.TryParseLine(zoneLine, out _));
    }

    [Fact]
    public void Combat_shaped_unparsed_lines_are_identifiable()
    {
        const string line = "2026-08-04 12:00:16 You take 12 points of Toxic damage from Poison Gas.";
        var parserEvent = CombatEventParserTestSupport.Classify(line);

        Assert.True(CombatEventParser.IsCombatShapedUnparsed(parserEvent));
    }
}

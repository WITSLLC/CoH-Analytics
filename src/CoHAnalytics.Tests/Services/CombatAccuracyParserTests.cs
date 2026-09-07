using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CombatAccuracyParserTests
{
    [Fact]
    public void Rolled_hit_parses_exact_attack_resolution_fields()
    {
        const string line =
            "2026-08-06 12:00:00 HIT Rikti Pylon! Your Flashfire power had a 95.00% chance to hit, you rolled a 51.51.";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(CombatEventKind.AttackResolution, combatEvent.Kind);
        Assert.Equal(CombatGrammarId.Acc01RolledHit, combatEvent.GrammarId);
        Assert.Equal(CombatAttackOutcome.Hit, combatEvent.AttackOutcome);
        Assert.Equal("Rikti Pylon", combatEvent.TargetName);
        Assert.Equal("Flashfire", combatEvent.PowerName);
        Assert.Equal(9500, combatEvent.DisplayedChanceHundredths);
        Assert.Equal(5151, combatEvent.RollHundredths);
        Assert.True(combatEvent.WasRolled);
        Assert.False(combatEvent.WasForced);
        Assert.False(combatEvent.IsAutohit);
    }

    [Fact]
    public void Rolled_miss_parses_exact_attack_resolution_fields()
    {
        const string line =
            "2026-08-06 12:00:02 MISSED Spirit!! Your Fire Cages power had a 95.00% chance to hit, you rolled a 97.54.";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(CombatEventKind.AttackResolution, combatEvent.Kind);
        Assert.Equal(CombatGrammarId.Acc02RolledMiss, combatEvent.GrammarId);
        Assert.Equal(CombatAttackOutcome.Miss, combatEvent.AttackOutcome);
        Assert.Equal("Spirit", combatEvent.TargetName);
        Assert.Equal("Fire Cages", combatEvent.PowerName);
        Assert.Equal(9500, combatEvent.DisplayedChanceHundredths);
        Assert.Equal(9754, combatEvent.RollHundredths);
        Assert.True(combatEvent.WasRolled);
    }

    [Fact]
    public void Forced_hit_parses_without_chance_or_roll()
    {
        const string line =
            "2026-08-06 12:00:04 HIT Lieutenant Skull! Your Fire Bolt power was forced to hit by streakbreaker.";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(CombatGrammarId.Acc03ForcedHit, combatEvent.GrammarId);
        Assert.Equal(CombatAttackOutcome.Hit, combatEvent.AttackOutcome);
        Assert.Equal("Lieutenant Skull", combatEvent.TargetName);
        Assert.Equal("Fire Bolt", combatEvent.PowerName);
        Assert.Null(combatEvent.DisplayedChanceHundredths);
        Assert.Null(combatEvent.RollHundredths);
        Assert.False(combatEvent.WasRolled);
        Assert.True(combatEvent.WasForced);
        Assert.False(combatEvent.IsAutohit);
    }

    [Fact]
    public void Autohit_parses_without_chance_or_roll()
    {
        const string line =
            "2026-08-06 12:00:05 HIT Training Dummy! Your Siphon Power power is autohit.";

        Assert.True(CombatEventParserTestSupport.TryParseLine(line, out var combatEvent));

        Assert.Equal(CombatGrammarId.Acc04Autohit, combatEvent.GrammarId);
        Assert.Equal(CombatAttackOutcome.Hit, combatEvent.AttackOutcome);
        Assert.False(combatEvent.WasRolled);
        Assert.False(combatEvent.WasForced);
        Assert.True(combatEvent.IsAutohit);
    }

    [Fact]
    public void Companion_miss_summary_does_not_emit_attack_resolution_event()
    {
        const string line = "2026-08-06 12:00:03 Fire Cages missed!";

        Assert.False(CombatEventParserTestSupport.TryParseLine(line, out _));
    }
}

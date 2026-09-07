using CoHAnalytics.Models;
using CoHAnalytics.Tests.Replay;

namespace CoHAnalytics.Tests.Services;

public sealed class CombatEventReplayOracleTests
{
    [Fact]
    public void Combat_core_grammar_fixture_produces_exact_event_sequence()
    {
        var fixturePath = ReplayTestPaths.Fixture("combat-core-grammar.log");
        var lines = File.ReadAllLines(fixturePath);
        var contextId = MonitoringContextId.CreateNew();
        var actual = CombatEventParserTestSupport.ParseFixtureLines(
            lines,
            new DateOnly(2026, 8, 4),
            contextId);

        var expected = BuildExpectedEvents(contextId);

        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            CombatEventParserTestSupport.AssertEquivalent(expected[index], actual[index]);
        }
    }

    [Fact]
    public void Combat_core_grammar_fixture_is_deterministic_across_replays()
    {
        var fixturePath = ReplayTestPaths.Fixture("combat-core-grammar.log");
        var lines = File.ReadAllLines(fixturePath);
        var contextId = MonitoringContextId.CreateNew();
        var first = CombatEventParserTestSupport.ParseFixtureLines(lines, new DateOnly(2026, 8, 4), contextId);
        var second = CombatEventParserTestSupport.ParseFixtureLines(lines, new DateOnly(2026, 8, 4), contextId);

        Assert.Equal(first.Count, second.Count);
        for (var index = 0; index < first.Count; index++)
        {
            CombatEventParserTestSupport.AssertEquivalent(first[index], second[index]);
        }
    }

    private static IReadOnlyList<CombatEvent> BuildExpectedEvents(MonitoringContextId contextId)
    {
        CombatEvent Base(
            long sequence,
            int second,
            CombatEventKind kind,
            CombatGrammarId grammarId,
            CombatActorRole actorRole,
            string? targetName = null,
            string? sourceName = null,
            string? powerName = null,
            long amountHundredths = 0,
            string? damageType = null,
            bool isOverTime = false,
            string? effectSuffix = null) =>
            new()
            {
                ContextId = contextId,
                ParserSequence = sequence,
                ObservedAt = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero).AddSeconds(sequence - 1),
                SourceTimestamp = new DateTime(2026, 8, 4, 12, 0, second),
                Kind = kind,
                GrammarId = grammarId,
                ActorRole = actorRole,
                TargetName = targetName,
                SourceName = sourceName,
                PowerName = powerName,
                Amount = new CombatScaledAmount(amountHundredths),
                DamageType = damageType,
                IsOverTime = isOverTime,
                EffectSuffix = effectSuffix
            };

        return
        [
            Base(1, 0, CombatEventKind.DamageDealt, CombatGrammarId.Dmg01YouHitWithPower, CombatActorRole.Self,
                targetName: "Lusca", powerName: "Hot Feet", amountHundredths: 1388, damageType: "Fire"),
            Base(2, 1, CombatEventKind.DamageDealt, CombatGrammarId.Dmg01YouHitWithPower, CombatActorRole.Self,
                targetName: "Lusca", powerName: "Fire Cages", amountHundredths: 559, damageType: "Fire", isOverTime: true),
            Base(3, 2, CombatEventKind.DamageDealt, CombatGrammarId.Dmg01YouHitWithPower, CombatActorRole.Self,
                targetName: "Lusca", powerName: "Fire Cages", amountHundredths: 1036, damageType: "Fire",
                isOverTime: true, effectSuffix: "CONTAINMENT"),
            Base(4, 3, CombatEventKind.DamageDealt, CombatGrammarId.Dmg01YouHitWithPower, CombatActorRole.Self,
                targetName: "Skull", powerName: "Fire Cages", amountHundredths: 821, damageType: "Fire"),
            Base(5, 4, CombatEventKind.DamageReceived, CombatGrammarId.Dmg03SourceHitsYouWithPower, CombatActorRole.Other,
                sourceName: "Crey Thorn Mook", powerName: "Bone Shard", amountHundredths: 2215, damageType: "Lethal"),
            Base(6, 5, CombatEventKind.DamageReceived, CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower,
                CombatActorRole.Other, sourceName: "Lieutenant Skull", powerName: "Sniper Rifle",
                amountHundredths: 5500, damageType: "Lethal"),
            Base(7, 6, CombatEventKind.HealingDealt, CombatGrammarId.Heal01YouHealTarget, CombatActorRole.Self,
                targetName: "Example Ally", powerName: "Healing Aura", amountHundredths: 7850),
            Base(8, 7, CombatEventKind.HealingReceived, CombatGrammarId.Heal03SourceHealsYou, CombatActorRole.Other,
                sourceName: "Example Medic", powerName: "Aid", amountHundredths: 4225),
            Base(9, 8, CombatEventKind.HealingDealt, CombatGrammarId.Heal02YouHealYourself, CombatActorRole.Self,
                targetName: "yourself", powerName: "Regeneration", amountHundredths: 1500),
            Base(10, 9, CombatEventKind.PowerActivation, CombatGrammarId.Act01YouActivate, CombatActorRole.Self,
                powerName: "Fire Cages"),
            Base(11, 10, CombatEventKind.PowerActivation, CombatGrammarId.Act02YouActivatedThePower, CombatActorRole.Self,
                powerName: "Fire Cages"),
            Base(12, 11, CombatEventKind.Defeat, CombatGrammarId.Def01YouHaveDefeated, CombatActorRole.Self,
                targetName: "Lusca"),
            Base(15, 14, CombatEventKind.DamageDealt, CombatGrammarId.Dmg01YouHitWithPower, CombatActorRole.Self,
                targetName: "O'Brien", powerName: "Scirocco's Dervish: Chance for Lethal Damage",
                amountHundredths: 4884, damageType: "Lethal"),
            Base(16, 15, CombatEventKind.DamageReceived, CombatGrammarId.Dmg04SourceHitsYouWithoutPower,
                CombatActorRole.Other, sourceName: "Fictional Target", amountHundredths: 1500, damageType: "Lethal")
        ];
    }
}

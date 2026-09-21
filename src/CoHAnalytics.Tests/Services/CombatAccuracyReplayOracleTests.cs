using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Replay;

namespace CoHAnalytics.Tests.Services;

public sealed class CombatAccuracyReplayOracleTests
{
    [Fact]
    public void Combat_accuracy_fixture_produces_exact_event_sequence()
    {
        var fixturePath = ReplayTestPaths.Fixture("combat-accuracy-grammar.log");
        var lines = File.ReadAllLines(fixturePath);
        var contextId = MonitoringContextId.CreateNew();
        var actual = CombatEventParserTestSupport.ParseFixtureLines(
            lines,
            new DateOnly(2026, 8, 6),
            contextId);

        var expected = BuildExpectedEvents(contextId);

        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            CombatEventParserTestSupport.AssertEquivalent(expected[index], actual[index]);
        }
    }

    [Fact]
    public void Combat_accuracy_fixture_produces_exact_session_accuracy_snapshot()
    {
        var fixturePath = ReplayTestPaths.Fixture("combat-accuracy-grammar.log");
        var lines = File.ReadAllLines(fixturePath);
        var contextId = MonitoringContextId.CreateNew();
        var events = CombatEventParserTestSupport.ParseFixtureLines(
            lines,
            new DateOnly(2026, 8, 6),
            contextId);

        var aggregator = new CombatAggregator();
        foreach (var combatEvent in events)
        {
            aggregator.Apply(combatEvent);
        }

        var referenceAt = new DateTimeOffset(2026, 8, 6, 12, 0, 20, TimeSpan.Zero);
        var snapshot = aggregator.ToSnapshot(
            new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero),
            referenceAt,
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);

        var expected = BuildExpectedAccuracySnapshot();
        AssertAccuracySnapshotEquivalent(expected, snapshot.Accuracy);

        var presentation = CombatAccuracyPresentation.Build(snapshot.Accuracy);
        Assert.Equal("8", presentation.AttemptsLabel);
        Assert.Equal("7", presentation.HitsLabel);
        Assert.Equal("1", presentation.MissesLabel);
        Assert.Equal("87.5%", presentation.HitPercentLabel);
        Assert.Equal("84.4%", presentation.AverageChanceLabel);
        Assert.Equal("49.1", presentation.AverageRollLabel);
    }

    internal static CombatAccuracyScopeSnapshot BuildExpectedAccuracySnapshot() =>
        new()
        {
            Attempts = 8,
            Hits = 7,
            Misses = 1,
            RolledAttempts = 7,
            DisplayedChanceSumHundredths = 59_090,
            RollSumHundredths = 34_337,
            ForcedHits = 1,
            Autohits = 1
        };

    internal static void AssertAccuracySnapshotEquivalent(
        CombatAccuracyScopeSnapshot expected,
        CombatAccuracyScopeSnapshot actual)
    {
        Assert.Equal(expected.Attempts, actual.Attempts);
        Assert.Equal(expected.Hits, actual.Hits);
        Assert.Equal(expected.Misses, actual.Misses);
        Assert.Equal(expected.RolledAttempts, actual.RolledAttempts);
        Assert.Equal(expected.DisplayedChanceSumHundredths, actual.DisplayedChanceSumHundredths);
        Assert.Equal(expected.RollSumHundredths, actual.RollSumHundredths);
        Assert.Equal(expected.ForcedHits, actual.ForcedHits);
        Assert.Equal(expected.Autohits, actual.Autohits);
    }

    private static IReadOnlyList<CombatEvent> BuildExpectedEvents(MonitoringContextId contextId)
    {
        CombatEvent Resolution(
            long sequence,
            int second,
            CombatGrammarId grammarId,
            CombatAttackOutcome outcome,
            string targetName,
            string powerName,
            bool wasRolled,
            bool wasForced = false,
            bool isAutohit = false,
            long? displayedChanceHundredths = null,
            long? rollHundredths = null) =>
            new()
            {
                ContextId = contextId,
                ParserSequence = sequence,
                ObservedAt = new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero).AddSeconds(sequence - 1),
                SourceTimestamp = new DateTime(2026, 8, 6, 12, 0, second),
                Kind = CombatEventKind.AttackResolution,
                GrammarId = grammarId,
                ActorRole = CombatActorRole.Self,
                TargetName = targetName,
                PowerName = powerName,
                AttackOutcome = outcome,
                DisplayedChanceHundredths = displayedChanceHundredths,
                RollHundredths = rollHundredths,
                WasRolled = wasRolled,
                WasForced = wasForced,
                IsAutohit = isAutohit
            };

        return
        [
            Resolution(1, 0, CombatGrammarId.Acc01RolledHit, CombatAttackOutcome.Hit, "Rikti Pylon", "Flashfire",
                wasRolled: true, displayedChanceHundredths: 9500, rollHundredths: 5151),
            Resolution(2, 1, CombatGrammarId.Acc01RolledHit, CombatAttackOutcome.Hit, "Elite Boss", "Flashfire",
                wasRolled: true, displayedChanceHundredths: 8000, rollHundredths: 1234),
            Resolution(3, 2, CombatGrammarId.Acc02RolledMiss, CombatAttackOutcome.Miss, "Spirit", "Fire Cages",
                wasRolled: true, displayedChanceHundredths: 9500, rollHundredths: 9754),
            Resolution(5, 4, CombatGrammarId.Acc03ForcedHit, CombatAttackOutcome.Hit, "Lieutenant Skull", "Fire Bolt",
                wasRolled: false, wasForced: true),
            Resolution(6, 5, CombatGrammarId.Acc04Autohit, CombatAttackOutcome.Hit, "Training Dummy", "Siphon Power",
                wasRolled: false, isAutohit: true),
            new()
            {
                ContextId = contextId,
                ParserSequence = 7,
                ObservedAt = new DateTimeOffset(2026, 8, 6, 12, 0, 6, TimeSpan.Zero),
                SourceTimestamp = new DateTime(2026, 8, 6, 12, 0, 6),
                Kind = CombatEventKind.PowerActivation,
                GrammarId = CombatGrammarId.Act01YouActivate,
                ActorRole = CombatActorRole.Self,
                PowerName = "Fire Cages",
                Amount = CombatScaledAmount.Zero
            },
            Resolution(8, 7, CombatGrammarId.Acc01RolledHit, CombatAttackOutcome.Hit, "Rikti Sapper A", "Fire Cages",
                wasRolled: true, displayedChanceHundredths: 7550, rollHundredths: 3086),
            Resolution(9, 8, CombatGrammarId.Acc01RolledHit, CombatAttackOutcome.Hit, "Rikti Sapper B", "Fire Cages",
                wasRolled: true, displayedChanceHundredths: 7550, rollHundredths: 4122),
            Resolution(10, 9, CombatGrammarId.Acc01RolledHit, CombatAttackOutcome.Hit, "Rikti Sapper C", "Fire Cages",
                wasRolled: true, displayedChanceHundredths: 7550, rollHundredths: 6010),
            new()
            {
                ContextId = contextId,
                ParserSequence = 11,
                ObservedAt = new DateTimeOffset(2026, 8, 6, 12, 0, 10, TimeSpan.Zero),
                SourceTimestamp = new DateTime(2026, 8, 6, 12, 0, 10),
                Kind = CombatEventKind.DamageDealt,
                GrammarId = CombatGrammarId.Dmg01YouHitWithPower,
                ActorRole = CombatActorRole.Self,
                TargetName = "O'Brien",
                PowerName = "Fire Cages",
                Amount = new CombatScaledAmount(2500),
                DamageType = "Fire"
            },
            Resolution(12, 11, CombatGrammarId.Acc01RolledHit, CombatAttackOutcome.Hit, "O'Brien", "Fire Cages",
                wasRolled: true, displayedChanceHundredths: 9440, rollHundredths: 4980)
        ];
    }
}

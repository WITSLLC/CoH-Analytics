using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class RollingCombatAccuracyTests
{
    private static readonly DateTimeOffset SessionStart = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rolling_window_aggregates_attempts_hits_misses_and_roll_totals()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddMinutes(2);

        rolling.Apply(Resolution(contextId, 1, SessionStart.AddSeconds(10), CombatAttackOutcome.Hit, wasRolled: true, 9500, 5151));
        rolling.Apply(Resolution(contextId, 2, SessionStart.AddSeconds(20), CombatAttackOutcome.Miss, wasRolled: true, 9500, 9754));
        rolling.Apply(Damage(contextId, 3, SessionStart.AddSeconds(30), 5_000));

        var window = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null).TenMinutes;
        var presentation = CombatAccuracyPresentation.Build(window.Accuracy);

        Assert.Equal(2, window.Accuracy.Attempts);
        Assert.Equal(1, window.Accuracy.Hits);
        Assert.Equal(1, window.Accuracy.Misses);
        Assert.Equal(2, window.Accuracy.RolledAttempts);
        Assert.Equal(19_000, window.Accuracy.DisplayedChanceSumHundredths);
        Assert.Equal(14_905, window.Accuracy.RollSumHundredths);
        Assert.Equal("50.0%", presentation.HitPercentLabel);
        Assert.Equal("95.0%", presentation.AverageChanceLabel);
        Assert.Equal("74.5", presentation.AverageRollLabel);
        Assert.Equal(new CombatScaledAmount(5_000), window.DamageDealt);
    }

    [Fact]
    public void Event_outside_one_minute_window_remains_in_longer_presets()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddMinutes(3);

        rolling.Apply(Resolution(contextId, 1, SessionStart.AddSeconds(30), CombatAttackOutcome.Hit, wasRolled: true, 9000, 5000));
        rolling.Apply(Damage(contextId, 2, SessionStart.AddMinutes(2).AddSeconds(30), 1_000));
        rolling.Apply(Resolution(contextId, 3, SessionStart.AddMinutes(2).AddSeconds(30), CombatAttackOutcome.Miss, wasRolled: true, 8000, 9000));

        var snapshot = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null);

        Assert.Equal(1, snapshot.OneMinute.Accuracy.Attempts);
        Assert.Equal(1, snapshot.OneMinute.Accuracy.Misses);
        Assert.Equal(2, snapshot.FiveMinutes.Accuracy.Attempts);
        Assert.Equal(1, snapshot.FiveMinutes.Accuracy.Hits);
        Assert.Equal(1, snapshot.FiveMinutes.Accuracy.Misses);
        Assert.Equal(2, snapshot.TenMinutes.Accuracy.Attempts);
        Assert.Equal(2, snapshot.FifteenMinutes.Accuracy.Attempts);
    }

    [Fact]
    public void All_five_presets_expose_independent_rolling_accuracy()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddMinutes(15);

        rolling.Apply(Resolution(contextId, 1, SessionStart.AddMinutes(14).AddSeconds(30), CombatAttackOutcome.Hit, wasRolled: true, 9000, 4000));
        rolling.Apply(Resolution(contextId, 2, SessionStart.AddMinutes(13), CombatAttackOutcome.Hit, wasRolled: true, 9000, 5000));
        rolling.Apply(Resolution(contextId, 3, SessionStart.AddMinutes(10), CombatAttackOutcome.Miss, wasRolled: true, 8000, 9000));
        rolling.Apply(Resolution(contextId, 4, SessionStart.AddMinutes(8), CombatAttackOutcome.Hit, wasRolled: true, 8500, 4500));
        rolling.Apply(Resolution(contextId, 5, SessionStart.AddMinutes(2), CombatAttackOutcome.Miss, wasRolled: true, 7500, 9500));
        rolling.Apply(Damage(contextId, 6, SessionStart.AddMinutes(14).AddSeconds(45), 2_000));

        var snapshot = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null);

        Assert.Equal(1, snapshot.OneMinute.Accuracy.Attempts);
        Assert.Equal(1, snapshot.OneMinute.Accuracy.Hits);
        Assert.Equal(2, snapshot.TwoMinutes.Accuracy.Attempts);
        Assert.Equal(3, snapshot.FiveMinutes.Accuracy.Attempts);
        Assert.Equal(4, snapshot.TenMinutes.Accuracy.Attempts);
        Assert.Equal(5, snapshot.FifteenMinutes.Accuracy.Attempts);
    }

    [Fact]
    public void Forced_hit_and_autohit_follow_session_semantics()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddMinutes(2);

        rolling.Apply(ForcedHit(contextId, 1, SessionStart.AddSeconds(10)));
        rolling.Apply(Autohit(contextId, 2, SessionStart.AddSeconds(20)));
        rolling.Apply(Damage(contextId, 3, SessionStart.AddSeconds(30), 1_000));

        var accuracy = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null).TenMinutes.Accuracy;

        Assert.Equal(1, accuracy.Attempts);
        Assert.Equal(1, accuracy.Hits);
        Assert.Equal(0, accuracy.Misses);
        Assert.Equal(0, accuracy.RolledAttempts);
        Assert.Equal(1, accuracy.ForcedHits);
        Assert.Equal(1, accuracy.Autohits);
    }

    [Fact]
    public void Empty_window_returns_not_observed_accuracy_without_false_zeros()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddMinutes(2);

        rolling.Apply(Damage(contextId, 1, SessionStart.AddSeconds(30), 3_000));

        var window = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null).TenMinutes;
        var presentation = CombatAccuracyPresentation.Build(window.Accuracy);

        Assert.Equal(CombatAccuracyScopeSnapshot.Empty, window.Accuracy);
        Assert.Equal(CombatAccuracyAvailability.NotObserved, presentation.Availability);
        Assert.False(presentation.ShowMetrics);
        Assert.Equal("—", presentation.HitPercentLabel);
    }

    [Fact]
    public void Multi_context_rolling_accuracy_remains_isolated()
    {
        var rollingA = new RollingCombatAccumulator();
        var rollingB = new RollingCombatAccumulator();
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddMinutes(2);

        rollingA.Apply(Resolution(contextA, 1, SessionStart.AddSeconds(20), CombatAttackOutcome.Hit, wasRolled: true, 9500, 5151));
        rollingA.Apply(Damage(contextA, 2, SessionStart.AddSeconds(30), 4_000));

        rollingB.Apply(Resolution(contextB, 1, SessionStart.AddSeconds(20), CombatAttackOutcome.Miss, wasRolled: true, 9500, 9754));
        rollingB.Apply(Damage(contextB, 2, SessionStart.AddSeconds(30), 9_000));

        var accuracyA = rollingA.ToSnapshot(SessionStart, reference, timingEndAt: null).TenMinutes.Accuracy;
        var accuracyB = rollingB.ToSnapshot(SessionStart, reference, timingEndAt: null).TenMinutes.Accuracy;

        Assert.Equal(1, accuracyA.Attempts);
        Assert.Equal(1, accuracyA.Hits);
        Assert.Equal(1, accuracyB.Attempts);
        Assert.Equal(1, accuracyB.Misses);
    }

    [Fact]
    public void Existing_rolling_damage_and_defeat_metrics_remain_correct()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddMinutes(2);

        rolling.Apply(Damage(contextId, 1, SessionStart.AddSeconds(10), 6_000));
        rolling.Apply(Defeat(contextId, 2, SessionStart.AddSeconds(20), CombatActorRole.Self, "Sprocket"));
        rolling.Apply(Defeat(contextId, 3, SessionStart.AddSeconds(30), CombatActorRole.Other, "Prototype Oscillator", "Psiche"));
        rolling.Apply(Resolution(contextId, 4, SessionStart.AddSeconds(40), CombatAttackOutcome.Hit, wasRolled: true, 9000, 5000));

        var window = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null).TenMinutes;

        Assert.Equal(new CombatScaledAmount(6_000), window.DamageDealt);
        Assert.True(window.DamagePerSecondHundredths > 0);
        Assert.Equal(2, window.TotalDefeated);
        Assert.Equal(1, window.MyDefeats);
        Assert.Equal(1, window.Accuracy.Attempts);
    }

    [Fact]
    public void New_gameplay_session_gets_empty_rolling_accuracy_buffer()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();
        aggregator.Apply(Resolution(contextId, 1, SessionStart.AddSeconds(10), CombatAttackOutcome.Hit, wasRolled: true, 9000, 5000));
        aggregator.Apply(Damage(contextId, 2, SessionStart.AddSeconds(20), 5_000));

        var fresh = new CombatAggregator();
        var snapshot = fresh.ToSnapshot(SessionStart.AddMinutes(5), SessionStart.AddMinutes(6), null, CombatActivityDefaults.IdleThreshold);

        Assert.Equal(CombatAccuracyScopeSnapshot.Empty, snapshot.Rolling.TenMinutes.Accuracy);
    }

    private static CombatEvent Damage(
        MonitoringContextId contextId,
        long sequence,
        DateTimeOffset at,
        long amountHundredths) =>
        new()
        {
            ContextId = contextId,
            ParserSequence = sequence,
            ObservedAt = at,
            SourceTimestamp = at.LocalDateTime,
            Kind = CombatEventKind.DamageDealt,
            GrammarId = CombatGrammarId.Dmg01YouHitWithPower,
            Amount = new CombatScaledAmount(amountHundredths)
        };

    private static CombatEvent Defeat(
        MonitoringContextId contextId,
        long sequence,
        DateTimeOffset at,
        CombatActorRole actorRole,
        string targetName,
        string? sourceName = null) =>
        new()
        {
            ContextId = contextId,
            ParserSequence = sequence,
            ObservedAt = at,
            SourceTimestamp = at.LocalDateTime,
            Kind = CombatEventKind.Defeat,
            GrammarId = actorRole == CombatActorRole.Self
                ? CombatGrammarId.Def01YouHaveDefeated
                : CombatGrammarId.Def02OtherPlayerDefeated,
            ActorRole = actorRole,
            TargetName = targetName,
            SourceName = sourceName
        };

    private static CombatEvent Resolution(
        MonitoringContextId contextId,
        long sequence,
        DateTimeOffset at,
        CombatAttackOutcome outcome,
        bool wasRolled = false,
        long? displayedChanceHundredths = null,
        long? rollHundredths = null) =>
        new()
        {
            ContextId = contextId,
            ParserSequence = sequence,
            ObservedAt = at,
            SourceTimestamp = at.LocalDateTime,
            Kind = CombatEventKind.AttackResolution,
            GrammarId = outcome == CombatAttackOutcome.Miss
                ? CombatGrammarId.Acc02RolledMiss
                : CombatGrammarId.Acc01RolledHit,
            AttackOutcome = outcome,
            DisplayedChanceHundredths = displayedChanceHundredths,
            RollHundredths = rollHundredths,
            WasRolled = wasRolled
        };

    private static CombatEvent ForcedHit(MonitoringContextId contextId, long sequence, DateTimeOffset at) =>
        new()
        {
            ContextId = contextId,
            ParserSequence = sequence,
            ObservedAt = at,
            SourceTimestamp = at.LocalDateTime,
            Kind = CombatEventKind.AttackResolution,
            GrammarId = CombatGrammarId.Acc03ForcedHit,
            AttackOutcome = CombatAttackOutcome.Hit,
            WasForced = true
        };

    private static CombatEvent Autohit(MonitoringContextId contextId, long sequence, DateTimeOffset at) =>
        new()
        {
            ContextId = contextId,
            ParserSequence = sequence,
            ObservedAt = at,
            SourceTimestamp = at.LocalDateTime,
            Kind = CombatEventKind.AttackResolution,
            GrammarId = CombatGrammarId.Acc04Autohit,
            AttackOutcome = CombatAttackOutcome.Hit,
            IsAutohit = true
        };
}

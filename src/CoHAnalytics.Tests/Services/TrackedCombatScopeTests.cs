using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class TrackedCombatScopeTests
{
    private static readonly DateTimeOffset Base = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_after_session_damage_leaves_tracked_at_zero()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();

        aggregator.Apply(Damage(contextId, 1, Base, 10000));
        aggregator.Tracked.Start(Base.AddSeconds(10));

        var snapshot = aggregator.ToSnapshot(Base, Base.AddSeconds(20), null, CombatActivityDefaults.IdleThreshold);

        Assert.Equal(new CombatScaledAmount(10000), snapshot.DamageDealt);
        Assert.Equal(CombatScaledAmount.Zero, snapshot.Tracked.DamageDealt);
        Assert.True(snapshot.Tracked.IsTracking);
    }

    [Fact]
    public void Running_pause_resume_and_stop_exclude_paused_damage_and_elapsed_time()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();
        var start = Base;

        aggregator.Apply(Damage(contextId, 1, start, 10000));
        aggregator.Tracked.Start(start);

        aggregator.Apply(Damage(contextId, 2, start.AddSeconds(15), 5000));
        aggregator.Tracked.Apply(Damage(contextId, 2, start.AddSeconds(15), 5000));
        aggregator.Tracked.Pause(start.AddSeconds(30));
        aggregator.Apply(Damage(contextId, 3, start.AddSeconds(45), 50000));
        aggregator.Tracked.Resume(start.AddSeconds(90));
        aggregator.Apply(Damage(contextId, 4, start.AddSeconds(105), 2500));
        aggregator.Tracked.Apply(Damage(contextId, 4, start.AddSeconds(105), 2500));
        aggregator.Tracked.Stop(start.AddSeconds(120));

        var snapshot = aggregator.ToSnapshot(start, start.AddSeconds(120), null, CombatActivityDefaults.IdleThreshold);

        Assert.Equal(new CombatScaledAmount(67500), snapshot.DamageDealt);
        Assert.Equal(new CombatScaledAmount(7500), snapshot.Tracked.DamageDealt);
        Assert.Equal(TimeSpan.FromSeconds(60), snapshot.Tracked.ActiveElapsed);
        Assert.False(snapshot.Tracked.IsTracking);
        Assert.Equal(
            125,
            snapshot.Tracked.DamagePerSecondHundredths);
    }

    [Fact]
    public void Pause_freezes_all_tracked_health_flow_totals()
    {
        var tracked = new TrackedCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var at = Base;

        tracked.Start(at);
        tracked.Apply(Event(contextId, 1, CombatEventKind.DamageReceived, at.AddSeconds(1), 1000));
        tracked.Apply(Event(contextId, 2, CombatEventKind.HealingDealt, at.AddSeconds(2), 2000));
        tracked.Apply(Event(contextId, 3, CombatEventKind.HealingReceived, at.AddSeconds(3), 3000));
        tracked.Apply(Event(contextId, 4, CombatEventKind.Defeat, at.AddSeconds(4)));
        tracked.Apply(Event(contextId, 5, CombatEventKind.PowerActivation, at.AddSeconds(5)));
        tracked.Pause(at.AddSeconds(10));

        tracked.Apply(Event(contextId, 6, CombatEventKind.DamageDealt, at.AddSeconds(11), 9000));
        tracked.Apply(Event(contextId, 7, CombatEventKind.DamageReceived, at.AddSeconds(12), 8000));

        var snapshot = tracked.ToSnapshot(at.AddSeconds(20));

        Assert.Equal(new CombatScaledAmount(0), snapshot.DamageDealt);
        Assert.Equal(new CombatScaledAmount(1000), snapshot.DamageReceived);
        Assert.Equal(new CombatScaledAmount(2000), snapshot.HealingDealt);
        Assert.Equal(new CombatScaledAmount(3000), snapshot.HealingReceived);
        Assert.Equal(1, snapshot.TotalDefeated);
        Assert.Equal(1, snapshot.MyDefeats);
        Assert.Equal(1, snapshot.PowerActivations);
    }

    [Fact]
    public void Stop_freezes_tracked_totals_against_later_session_events()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();

        aggregator.Tracked.Start(Base);
        var trackedDamage = Damage(contextId, 1, Base.AddSeconds(5), 5000);
        aggregator.Tracked.Apply(trackedDamage);
        aggregator.Tracked.Stop(Base.AddSeconds(10));
        aggregator.Apply(Damage(contextId, 2, Base.AddSeconds(20), 7000));

        var snapshot = aggregator.ToSnapshot(Base, Base.AddSeconds(30), null, CombatActivityDefaults.IdleThreshold);

        Assert.Equal(new CombatScaledAmount(7000), snapshot.DamageDealt);
        Assert.Equal(new CombatScaledAmount(5000), snapshot.Tracked.DamageDealt);
    }

    private static CombatEvent Damage(
        MonitoringContextId contextId,
        long sequence,
        DateTimeOffset observedAt,
        long amountHundredths) =>
        Event(contextId, sequence, CombatEventKind.DamageDealt, observedAt, amountHundredths);

    private static CombatEvent Event(
        MonitoringContextId contextId,
        long sequence,
        CombatEventKind kind,
        DateTimeOffset observedAt,
        long amountHundredths = 0) =>
        new()
        {
            ContextId = contextId,
            ParserSequence = sequence,
            ObservedAt = observedAt,
            SourceTimestamp = observedAt.DateTime,
            Kind = kind,
            GrammarId = CombatGrammarId.Dmg01YouHitWithPower,
            ActorRole = CombatActorRole.Self,
            Amount = new CombatScaledAmount(amountHundredths)
        };
}

using System.Diagnostics;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Replay;

namespace CoHAnalytics.Tests.Services;

public sealed class CombatAggregationSessionTests
{
    [Fact]
    public async Task Multi_context_combat_totals_remain_isolated()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

        try
        {
            var contextA = MonitoringContextId.CreateNew();
            var contextB = MonitoringContextId.CreateNew();
            var sourceA = GameplaySessionTestInfrastructure.DefaultSource("acct-a");
            var sourceB = GameplaySessionTestInfrastructure.DefaultSource("acct-b");

            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextA, sourceA),
                GameplaySessionTestInfrastructure.ReadyContext(contextB, sourceB)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                new GameplaySessionOptions
                {
                    TimeProvider = time,
                    CombatSnapshotPublishInterval = TimeSpan.Zero
                });

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:00 Welcome to City of Heroes, Hero A!",
                    contextA,
                    sourceA,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:00 Welcome to City of Heroes, Hero B!",
                    contextB,
                    sourceB,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextA
                    && session.CharacterRecordId is not null
                    && session.CharacterDisplayName == "Hero A")
                && manager.Current.Sessions.Any(session =>
                    session.ContextId == contextB
                    && session.CharacterRecordId is not null
                    && session.CharacterDisplayName == "Hero B"));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:01 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
                    contextA,
                    sourceA,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:01 You hit Skull with your Fire Cages for 8.21 points of Fire damage.",
                    contextB,
                    sourceB,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextA
                    && session.Combat.DamageDealt == new CombatScaledAmount(1388))
                && manager.Current.Sessions.Any(session =>
                    session.ContextId == contextB
                    && session.Combat.DamageDealt == new CombatScaledAmount(821)));

            var sessionA = manager.Current.Sessions.Single(session => session.ContextId == contextA);
            var sessionB = manager.Current.Sessions.Single(session => session.ContextId == contextB);

            Assert.Equal(new CombatScaledAmount(1388), sessionA.Combat.DamageDealt);
            Assert.Equal(new CombatScaledAmount(821), sessionB.Combat.DamageDealt);
            Assert.Equal(CombatScaledAmount.Zero, sessionA.Combat.DamageReceived);
            Assert.Equal(CombatScaledAmount.Zero, sessionB.Combat.HealingDealt);

            await manager.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task Different_character_handoff_resets_combat_totals_on_same_context()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();

            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                new GameplaySessionOptions
                {
                    TimeProvider = time,
                    CombatSnapshotPublishInterval = TimeSpan.Zero
                });

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:00 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1,
                    observedAt: time.GetUtcNow())
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.CharacterRecordId is not null
                    && session.CharacterDisplayName == "Example Hero"));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:01 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
                    contextId,
                    source,
                    sequence: 2,
                    observedAt: time.GetUtcNow().AddSeconds(1))
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.Combat.DamageDealt == new CombatScaledAmount(1388)));

            var firstSession = Assert.Single(manager.Current.Sessions);
            time.Advance(TimeSpan.FromSeconds(30));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:30 Welcome to City of Heroes, Another Hero!",
                    contextId,
                    source,
                    sequence: 3,
                    observedAt: time.GetUtcNow())
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            {
                var session = manager.Current.Sessions.SingleOrDefault();
                return session is not null
                    && session.SessionId != firstSession.SessionId
                    && session.CharacterDisplayName == "Another Hero"
                    && session.Combat.DamageDealt == CombatScaledAmount.Zero;
            });

            var session = manager.Current.Sessions.Single();
            Assert.NotEqual(firstSession.SessionId, session.SessionId);
            Assert.Equal(time.GetUtcNow(), session.StartedAt);
            Assert.Equal(CombatScaledAmount.Zero, session.Combat.DamageDealt);
            Assert.Equal(0, session.Combat.PowerActivations);

            await manager.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task Dense_combat_burst_coalesces_snapshot_publications()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));
        var publications = 0L;

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();

            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                new GameplaySessionOptions
                {
                    TimeProvider = time,
                    CombatSnapshotPublishInterval = TimeSpan.FromSeconds(1),
                    TestHooks = new GameplaySessionTestHooks
                    {
                        OnSnapshotPublished = () => Interlocked.Increment(ref publications)
                    }
                });

            manager.StateChanged += (_, _) => { };

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:00 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.CharacterRecordId is not null
                    && session.CharacterDisplayName == "Example Hero"));
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            {
                var diagnostics = manager.GetDiagnostics();
                return diagnostics.WorkQueue.IsDrained
                    && diagnostics.LastAcceptedWorkSequence == diagnostics.LastCompletedWorkSequence;
            });
            Assert.True(Volatile.Read(ref publications) > 0);
            Volatile.Write(ref publications, 0);

            const int burstCount = 500;
            for (var index = 0; index < burstCount; index++)
            {
                parser.PublishClassified([
                    GameplaySessionTestInfrastructure.Classify(
                        "2026-08-04 12:00:01 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
                        contextId,
                        source,
                        sequence: index + 2)
                ]);
            }

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            {
                var diagnostics = manager.GetDiagnostics();
                return diagnostics.WorkQueue.IsDrained
                    && diagnostics.LastAcceptedWorkSequence == diagnostics.LastCompletedWorkSequence;
            });
            Assert.Equal(0, Volatile.Read(ref publications));

            time.Advance(TimeSpan.FromSeconds(1));
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:02 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
                    contextId,
                    source,
                    sequence: burstCount + 2)
            ]);

            var expectedDamage = new CombatScaledAmount(1388L * (burstCount + 1));
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                Volatile.Read(ref publications) >= 1
                && manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.Combat.DamageDealt == expectedDamage));

            var session = manager.Current.Sessions.Single(item => item.ContextId == contextId);
            var combatPublications = Volatile.Read(ref publications);
            Assert.Equal(expectedDamage, session.Combat.DamageDealt);
            Assert.True((burstCount + 1) > combatPublications * 10,
                $"Expected events >> publications, events={burstCount + 1}, publications={combatPublications}");

            await manager.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Combat_aggregation_throughput_baseline_reports_events_and_publication_counts()
    {
        var fixturePath = ReplayTestPaths.Fixture("combat-core-grammar.log");
        var lines = File.ReadAllLines(fixturePath);
        var contextId = MonitoringContextId.CreateNew();
        var events = CombatEventParserTestSupport.ParseFixtureLines(
            lines,
            new DateOnly(2026, 8, 4),
            contextId);

        const int iterations = 5_000;
        var correctnessAggregator = new CombatAggregator();
        foreach (var combatEvent in events)
        {
            correctnessAggregator.Apply(combatEvent);
        }

        var referenceAt = new DateTimeOffset(2026, 8, 4, 12, 0, 20, TimeSpan.Zero);
        CombatAggregationReplayOracleTests.AssertCombatSnapshotEquivalent(
            CombatAggregationReplayOracleTests.BuildExpectedFixtureSnapshot(referenceAt),
            correctnessAggregator.ToSnapshot(
                new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
                referenceAt,
                null,
                CombatActivityDefaults.IdleThreshold));

        var aggregator = new CombatAggregator();
        var stopwatch = Stopwatch.StartNew();

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            foreach (var combatEvent in events)
            {
                aggregator.Apply(combatEvent);
            }
        }

        stopwatch.Stop();

        var eventsApplied = aggregator.EventsApplied;
        var eventsPerSecond = eventsApplied / stopwatch.Elapsed.TotalSeconds;

        Assert.True(eventsPerSecond > 100_000, $"Expected at least 100k events/sec, observed {eventsPerSecond:N0}");

        AggregationThroughputBaseline = new CombatAggregationThroughputBaseline(
            EventsApplied: eventsApplied,
            Elapsed: stopwatch.Elapsed,
            EventsPerSecond: eventsPerSecond);
    }

    internal static CombatAggregationThroughputBaseline? AggregationThroughputBaseline { get; private set; }

    internal sealed record CombatAggregationThroughputBaseline(
        long EventsApplied,
        TimeSpan Elapsed,
        double EventsPerSecond);
}

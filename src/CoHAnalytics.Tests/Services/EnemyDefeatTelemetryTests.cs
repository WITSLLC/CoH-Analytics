using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Replay;

namespace CoHAnalytics.Tests.Services;

public sealed class EnemyDefeatTelemetryTests
{
    private static readonly DateTimeOffset SessionStart = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Session_totals_split_player_and_team_defeats()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();

        aggregator.Apply(Defeat(contextId, 1, SessionStart.AddSeconds(1), CombatActorRole.Self, "Sprocket"));
        aggregator.Apply(Defeat(contextId, 2, SessionStart.AddSeconds(2), CombatActorRole.Other, "Prototype Oscillator", "Psiche"));
        aggregator.Apply(Defeat(contextId, 3, SessionStart.AddSeconds(3), CombatActorRole.Other, "Prototype Oscillator", "Trapperkeeper"));

        var snapshot = aggregator.ToSnapshot(
            SessionStart,
            SessionStart.AddSeconds(10),
            null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(3, snapshot.TotalDefeated);
        Assert.Equal(1, snapshot.MyDefeats);
    }

    [Fact]
    public void Tracked_session_counts_defeats_only_while_running()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();

        aggregator.Apply(Defeat(contextId, 1, SessionStart.AddSeconds(1), CombatActorRole.Self, "Sprocket"));
        aggregator.Tracked.Start(SessionStart.AddSeconds(2));
        aggregator.Apply(Defeat(contextId, 2, SessionStart.AddSeconds(3), CombatActorRole.Self, "Sprocket"));
        aggregator.Tracked.Apply(Defeat(contextId, 2, SessionStart.AddSeconds(3), CombatActorRole.Self, "Sprocket"));
        aggregator.Apply(Defeat(contextId, 3, SessionStart.AddSeconds(4), CombatActorRole.Other, "Prototype Oscillator", "Psiche"));
        aggregator.Tracked.Apply(Defeat(contextId, 3, SessionStart.AddSeconds(4), CombatActorRole.Other, "Prototype Oscillator", "Psiche"));
        aggregator.Tracked.Pause(SessionStart.AddSeconds(5));
        aggregator.Apply(Defeat(contextId, 4, SessionStart.AddSeconds(6), CombatActorRole.Self, "Prototype Oscillator"));
        aggregator.Tracked.Apply(Defeat(contextId, 4, SessionStart.AddSeconds(6), CombatActorRole.Self, "Prototype Oscillator"));

        var snapshot = aggregator.ToSnapshot(
            SessionStart,
            SessionStart.AddSeconds(10),
            null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(4, snapshot.TotalDefeated);
        Assert.Equal(3, snapshot.MyDefeats);
        Assert.Equal(2, snapshot.Tracked.TotalDefeated);
        Assert.Equal(1, snapshot.Tracked.MyDefeats);
    }

    [Fact]
    public void Rolling_window_aggregates_total_and_my_defeats()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddMinutes(2);

        rolling.Apply(Defeat(contextId, 1, SessionStart.AddSeconds(30), SessionStart.AddSeconds(30), CombatActorRole.Self, "Sprocket"));
        rolling.Apply(Defeat(
            contextId,
            2,
            SessionStart.AddSeconds(45),
            SessionStart.AddSeconds(45),
            CombatActorRole.Other,
            "Prototype Oscillator",
            "Psiche"));
        rolling.Apply(Defeat(
            contextId,
            3,
            SessionStart.AddMinutes(1).AddSeconds(30),
            SessionStart.AddMinutes(1).AddSeconds(30),
            CombatActorRole.Self,
            "Prototype Oscillator"));

        var window = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null).TenMinutes;

        Assert.Equal(3, window.TotalDefeated);
        Assert.Equal(2, window.MyDefeats);
    }

    [Fact]
    public void Multi_context_defeat_totals_remain_isolated()
    {
        var aggregatorA = new CombatAggregator();
        var aggregatorB = new CombatAggregator();
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();

        aggregatorA.Apply(Defeat(contextA, 1, SessionStart.AddSeconds(1), CombatActorRole.Self, "Sprocket"));
        aggregatorA.Apply(Defeat(contextA, 2, SessionStart.AddSeconds(2), CombatActorRole.Other, "Prototype Oscillator", "Psiche"));

        aggregatorB.Apply(Defeat(contextB, 1, SessionStart.AddSeconds(1), CombatActorRole.Other, "Prototype Oscillator", "Trapperkeeper"));
        aggregatorB.Apply(Defeat(contextB, 2, SessionStart.AddSeconds(2), CombatActorRole.Self, "Sprocket"));
        aggregatorB.Apply(Defeat(contextB, 3, SessionStart.AddSeconds(3), CombatActorRole.Self, "Sprocket"));

        var snapshotA = aggregatorA.ToSnapshot(SessionStart, SessionStart.AddSeconds(10), null, CombatActivityDefaults.IdleThreshold);
        var snapshotB = aggregatorB.ToSnapshot(SessionStart, SessionStart.AddSeconds(10), null, CombatActivityDefaults.IdleThreshold);

        Assert.Equal(2, snapshotA.TotalDefeated);
        Assert.Equal(1, snapshotA.MyDefeats);
        Assert.Equal(3, snapshotB.TotalDefeated);
        Assert.Equal(2, snapshotB.MyDefeats);
    }

    [Fact]
    public async Task Character_handoff_resets_defeat_totals_on_same_context()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var time = new ManualTimeProvider(SessionStart);

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
                    && session.CharacterRecordId is not null));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:01 You have defeated Sprocket",
                    contextId,
                    source,
                    sequence: 2,
                    observedAt: time.GetUtcNow().AddSeconds(1)),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:02 Psiche has defeated Prototype Oscillator",
                    contextId,
                    source,
                    sequence: 3,
                    observedAt: time.GetUtcNow().AddSeconds(2))
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.Combat.TotalDefeated == 2
                    && session.Combat.MyDefeats == 1));

            var firstSession = Assert.Single(manager.Current.Sessions);
            time.Advance(TimeSpan.FromSeconds(30));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:30 Welcome to City of Heroes, Another Hero!",
                    contextId,
                    source,
                    sequence: 4,
                    observedAt: time.GetUtcNow())
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            {
                var session = manager.Current.Sessions.SingleOrDefault();
                return session is not null
                    && session.SessionId != firstSession.SessionId
                    && session.Combat.TotalDefeated == 0
                    && session.Combat.MyDefeats == 0;
            });

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
    public void Parser_fixture_lines_produce_expected_attribution()
    {
        var lines = new[]
        {
            "2026-08-04 12:00:00 You have defeated Sprocket",
            "2026-08-04 12:00:01 Psiche has defeated Prototype Oscillator",
            "2026-08-04 12:00:02 Trapperkeeper has defeated Prototype Oscillator"
        };

        var events = CombatEventParserTestSupport.ParseFixtureLines(lines, new DateOnly(2026, 8, 4));
        Assert.Equal(3, events.Count);

        var total = events.Count(e => e.Kind == CombatEventKind.Defeat);
        var mine = events.Count(e => e.Kind == CombatEventKind.Defeat && e.ActorRole == CombatActorRole.Self);

        Assert.Equal(3, total);
        Assert.Equal(1, mine);
    }

    private static CombatEvent Defeat(
        MonitoringContextId contextId,
        long sequence,
        DateTimeOffset observedAt,
        DateTimeOffset sourceAt,
        CombatActorRole actorRole,
        string targetName,
        string? sourceName = null) =>
        new()
        {
            ContextId = contextId,
            ParserSequence = sequence,
            ObservedAt = observedAt,
            SourceTimestamp = sourceAt.LocalDateTime,
            Kind = CombatEventKind.Defeat,
            GrammarId = actorRole == CombatActorRole.Self
                ? CombatGrammarId.Def01YouHaveDefeated
                : CombatGrammarId.Def02OtherPlayerDefeated,
            ActorRole = actorRole,
            TargetName = targetName,
            SourceName = sourceName
        };

    private static CombatEvent Defeat(
        MonitoringContextId contextId,
        long sequence,
        DateTimeOffset at,
        CombatActorRole actorRole,
        string targetName,
        string? sourceName = null) =>
        Defeat(contextId, sequence, at, at, actorRole, targetName, sourceName);
}

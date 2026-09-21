using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionTelemetryTests
{
    [Fact]
    public async Task Untimestamped_welcome_and_telemetry_resolve_confirmed_identity_and_session_totals()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var observedAt = new DateTimeOffset(2026, 8, 7, 3, 57, 0, TimeSpan.Zero);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = LogSourceId.Create(
                "TestAccount",
                "TestAccount",
                @"C:\fake\TestAccount\Logs\chatlog 2026-08-07.txt",
                new DateOnly(2026, 8, 7));
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "Welcome to City of Heroes, Dawn's Vanguard!",
                    contextId,
                    source,
                    sequence: 1,
                    observedAt: observedAt),
                GameplaySessionTestInfrastructure.Classify(
                    "You gain 1,234 experience and 567 influence.",
                    contextId,
                    source,
                    sequence: 2,
                    observedAt: observedAt.AddSeconds(1))
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.CharacterIdentityConfidence == CharacterIdentityConfidence.Confirmed
                    && session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Resolved
                    && session.CharacterDisplayName == "Dawn's Vanguard"
                    && session.SessionExperienceGained == 1234
                    && session.SessionGameplayInfluenceGained == 567));
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
    public async Task Bracket_timestamp_telemetry_line_aggregates_on_session()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = LogSourceId.Create(
                "acct-1",
                "acct-1",
                @"C:\fake\chatlog 2026-08-04.txt",
                new DateOnly(2026, 8, 4));
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "[03:57] Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "[03:57] You gain 1,234 experience and 567 influence.",
                    contextId,
                    source,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "[03:58] You gain 500 experience.",
                    contextId,
                    source,
                    sequence: 3)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.SessionExperienceGained == 1734
                    && session.SessionGameplayInfluenceGained == 567));
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
    public async Task Committed_xp_and_influence_lines_aggregate_on_session()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

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
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 You gain 1,000 experience and 250 influence.",
                    contextId,
                    source,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:12 You gain 500 experience.",
                    contextId,
                    source,
                    sequence: 3)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session => session.SessionExperienceGained == 1500
                    && session.SessionGameplayInfluenceGained == 250));
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
    public async Task Retained_pre_identity_telemetry_commits_exactly_once_on_confirmation()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            repository.EstablishTrustedFromWelcome("acct-1", "Example Hero");
            repository.EstablishTrustedFromManualConfirmation("acct-1", "Other Hero");

            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 You gain 300 experience and 100 influence.",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.RetainedEventCount == 1));

            var otherRecord = repository.TryFindTrustedByDisplayName("acct-1", "Other Hero")!;
            var confirm = manager.ConfirmCharacter(contextId, otherRecord.RecordId);
            Assert.True(confirm.IsSuccess);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.SessionExperienceGained == 300
                    && session.SessionGameplayInfluenceGained == 100));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:02 You gain 300 experience and 100 influence.",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.SessionExperienceGained == 600
                    && session.SessionGameplayInfluenceGained == 200));
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
    public async Task Repeated_welcome_for_same_character_preserves_session_telemetry_totals()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

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
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 You gain 900 experience and 100 influence.",
                    contextId,
                    source,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:20 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:21 You gain 50 experience.",
                    contextId,
                    source,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.SessionExperienceGained == 950
                    && session.SessionGameplayInfluenceGained == 100));
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
    public async Task Retention_overflow_discarded_lines_do_not_affect_telemetry()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var options = new GameplaySessionOptions { MaxRetainedEventCount = 1 };
            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                options);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 You gain 100 experience.",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:02 You gain 200 experience.",
                    contextId,
                    source,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:03 You gain 300 experience.",
                    contextId,
                    source,
                    sequence: 3)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.RetentionOverflowed));

            var session = manager.Current.Sessions[0];
            Assert.Equal(0, session.SessionExperienceGained);
            Assert.Equal(0, session.SessionGameplayInfluenceGained);
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
    public async Task Multi_context_sessions_keep_isolated_telemetry_totals()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextA = MonitoringContextId.CreateNew();
            var contextB = MonitoringContextId.CreateNew();
            var sourceA = GameplaySessionTestInfrastructure.DefaultSource("acct-a");
            var sourceB = GameplaySessionTestInfrastructure.DefaultSource("acct-b");
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                [
                    GameplaySessionTestInfrastructure.ReadyContext(contextA, sourceA),
                    GameplaySessionTestInfrastructure.ReadyContext(contextB, sourceB)
                ]));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Hero A!",
                    contextA,
                    sourceA,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 You gain 100 experience.",
                    contextA,
                    sourceA,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Hero B!",
                    contextB,
                    sourceB,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 You gain 40 influence.",
                    contextB,
                    sourceB,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            {
                var sessions = manager.Current.Sessions;
                return sessions.Count == 2
                    && sessions.Any(session =>
                        session.ContextId == contextA
                        && session.SessionExperienceGained == 100
                        && session.SessionGameplayInfluenceGained == 0)
                    && sessions.Any(session =>
                        session.ContextId == contextB
                        && session.SessionExperienceGained == 0
                        && session.SessionGameplayInfluenceGained == 40);
            });
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
}

using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterObservedLevelTests
{
    private const string LevelImprovementLine =
        "Your combat improves to level 50! Seek a trainer to further your abilities.";

    private const string CombatScalingLine = "You are now fighting at level 50.";

    [Fact]
    public void Character_level_improvement_grammar_parses_observed_level()
    {
        var parser = new GameplayTelemetryParser();
        var classified = GameplaySessionTestInfrastructure.Classify(
            LevelImprovementLine,
            MonitoringContextId.CreateNew(),
            GameplaySessionTestInfrastructure.DefaultSource());

        Assert.True(parser.TryParse(classified, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Chr01CharacterLevelImprovement, observation.GrammarId);
        Assert.Equal(50, observation.CharacterObservedLevel);
    }

    [Fact]
    public void Combat_scaling_line_does_not_parse_character_level_improvement()
    {
        var parser = new GameplayTelemetryParser();
        var classified = GameplaySessionTestInfrastructure.Classify(
            CombatScalingLine,
            MonitoringContextId.CreateNew(),
            GameplaySessionTestInfrastructure.DefaultSource());

        Assert.False(parser.TryParse(classified, out _));
    }

    [Fact]
    public async Task Resolved_character_receives_observed_level_from_live_notification()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource("TestAccount");
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.CharacterRecordId is not null
                    && session.CharacterDisplayName == "Dawn's Vanguard"));
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);

            var recordId = manager.Current.Sessions
                .Single(session => session.ContextId == contextId)
                .CharacterRecordId;
            Assert.NotNull(recordId);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-04 06:27:01 {LevelImprovementLine}",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                repository.TryGetRecord(recordId!)?.ObservedLevel == 50);

            var record = repository.TryGetRecord(recordId!);
            Assert.NotNull(record);
            Assert.Equal(50, record!.ObservedLevel);
            Assert.Equal("Level 50", record.ObservedLevelLabel);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Unresolved_character_does_not_receive_observed_level()
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
                    $"2026-08-04 06:27:01 {LevelImprovementLine}",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => manager.Current.Sessions.Count == 1);

            var session = manager.Current.Sessions[0];
            Assert.Null(session.CharacterRecordId);
            Assert.Empty(repository.Current.Records);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Character_switch_updates_only_currently_resolved_character()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource("TestAccount");
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.CharacterRecordId is not null
                    && session.CharacterDisplayName == "Dawn's Vanguard"));
            var hellsRecordId = manager.Current.Sessions[0].CharacterRecordId;
            Assert.NotNull(hellsRecordId);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 Your combat improves to level 50! Seek a trainer to further your abilities.",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                repository.TryGetRecord(hellsRecordId!)?.ObservedLevel == 50);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:28:00 Welcome to City of Heroes, Alpha Hero!",
                    contextId,
                    source,
                    sequence: 3)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            {
                var session = manager.Current.Sessions.SingleOrDefault();
                return session is not null
                    && session.CharacterRecordId is not null
                    && session.CharacterRecordId != hellsRecordId;
            });

            var primaryRecordId = manager.Current.Sessions[0].CharacterRecordId;
            Assert.NotNull(primaryRecordId);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:28:01 Your combat improves to level 32! Seek a trainer to further your abilities.",
                    contextId,
                    source,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                repository.TryGetRecord(primaryRecordId!)?.ObservedLevel == 32);

            Assert.Equal(50, repository.TryGetRecord(hellsRecordId!)?.ObservedLevel);
            Assert.Equal(32, repository.TryGetRecord(primaryRecordId!)?.ObservedLevel);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Multi_context_level_notifications_remain_isolated()
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
                    "2026-08-04 06:27:00 Welcome to City of Heroes, Hero A!",
                    contextA,
                    sourceA,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:00 Welcome to City of Heroes, Hero B!",
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

            var recordA = manager.Current.Sessions
                .First(session => session.ContextId == contextA)
                .CharacterRecordId;
            var recordB = manager.Current.Sessions
                .First(session => session.ContextId == contextB)
                .CharacterRecordId;
            Assert.NotNull(recordA);
            Assert.NotNull(recordB);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 Your combat improves to level 40! Seek a trainer to further your abilities.",
                    contextA,
                    sourceA,
                    sequence: 3)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                repository.TryGetRecord(recordA!)?.ObservedLevel == 40);

            Assert.Equal(40, repository.TryGetRecord(recordA!)?.ObservedLevel);
            Assert.Null(repository.TryGetRecord(recordB!)?.ObservedLevel);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 Your combat improves to level 21! Seek a trainer to further your abilities.",
                    contextB,
                    sourceB,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                repository.TryGetRecord(recordB!)?.ObservedLevel == 21);

            Assert.Equal(40, repository.TryGetRecord(recordA!)?.ObservedLevel);
            Assert.Equal(21, repository.TryGetRecord(recordB!)?.ObservedLevel);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void Observed_level_persists_across_repository_instances()
    {
        var dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-character-level",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataDirectory);

        try
        {
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));
            var writer = new CharacterRepository(new CharacterRepositoryOptions
            {
                DataDirectory = dataDirectory,
                TimeProvider = time
            });

            var established = writer.EstablishTrustedFromWelcome("acct-1", "Dawn's Vanguard");
            Assert.True(established.IsSuccess);

            var observedAt = time.GetUtcNow();
            var result = writer.RecordObservedLevel(established.RecordId!, 50, observedAt);
            Assert.True(result.IsSuccess);

            var reader = new CharacterRepository(new CharacterRepositoryOptions
            {
                DataDirectory = dataDirectory,
                TimeProvider = time
            });

            var record = reader.TryGetRecord(established.RecordId!);
            Assert.NotNull(record);
            Assert.Equal(50, record!.ObservedLevel);
            Assert.Equal(observedAt, record.ObservedLevelObservedAt);
            Assert.Equal("Level 50", record.ObservedLevelLabel);
        }
        finally
        {
            TryDeleteDirectory(dataDirectory);
        }
    }

    [Fact]
    public void Character_without_observed_level_displays_level_unknown()
    {
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var established = repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");
            var record = repository.TryGetRecord(established.RecordId!);

            Assert.NotNull(record);
            Assert.Null(record!.ObservedLevel);
            Assert.Equal("Level Unknown", record.ObservedLevelLabel);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Combat_scaling_line_does_not_update_character_observed_level()
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
                    "2026-08-04 06:27:00 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-04 06:27:01 {CombatScalingLine}",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.GetDiagnostics().TotalCommittedEvents >= 2
                && manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.CharacterRecordId is not null
                    && session.CharacterDisplayName == "Example Hero"));
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);

            var recordId = manager.Current.Sessions
                .Single(session => session.ContextId == contextId)
                .CharacterRecordId;
            Assert.NotNull(recordId);
            Assert.Null(repository.TryGetRecord(recordId!)?.ObservedLevel);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
    }
}

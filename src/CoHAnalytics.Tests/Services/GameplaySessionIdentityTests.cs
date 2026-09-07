using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionIdentityTests
{
    [Fact]
    public async Task Welcome_confirms_identity_separate_from_repository_trust_axis()
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
                    source)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session =>
                    session.CharacterIdentityConfidence == CharacterIdentityConfidence.Confirmed
                    && session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Resolved));

            var record = repository.TryFindTrustedByDisplayName("acct-1", "Example Hero")!;
            Assert.Equal(CharacterTrustState.TrustedFromWelcome, record.TrustState);
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
    public async Task Established_identity_ignores_enemy_attributed_action_lines()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var committed = new List<GameplaySessionEvent>();

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
            manager.CommittedEventsAvailable += (_, args) => committed.AddRange(args.Events);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session =>
                    session.CharacterIdentityConfidence == CharacterIdentityConfidence.Confirmed));

            var enemyLines = Enumerable.Range(2, 20)
                .Select(sequence => GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-04 06:27:{sequence:D2} Enemy Name hits you with their effect.",
                    contextId,
                    source,
                    sequence))
                .ToArray();

            parser.PublishClassified(enemyLines);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => committed.Count >= 21);

            var session = manager.Current.Sessions[0];
            Assert.Equal(CharacterIdentityConfidence.Confirmed, session.CharacterIdentityConfidence);
            Assert.Equal(CharacterIdentityResolutionState.Resolved, session.CharacterIdentityResolutionState);
            Assert.Equal(0, session.CandidateCount);
            Assert.False(session.NeedsAttention);
            Assert.Equal(21, committed.Count);
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
    public async Task Different_attributed_actor_matching_trusted_record_does_not_switch_identity()
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

            repository.EstablishTrustedFromWelcome("acct-1", "Example Hero");
            repository.EstablishTrustedFromWelcome("acct-1", "Other Hero");

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
                    "2026-08-04 06:27:11 Other Hero hits you with their effect.",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => committedCount(manager) >= 2);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session =>
                    session.CharacterIdentityConfidence == CharacterIdentityConfidence.Confirmed));

            var session = manager.Current.Sessions[0];
            Assert.Equal(CharacterIdentityConfidence.Confirmed, session.CharacterIdentityConfidence);
            Assert.Equal(CharacterIdentityResolutionState.Resolved, session.CharacterIdentityResolutionState);
            Assert.Equal("Example Hero", session.CharacterDisplayName);
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

    private static long committedCount(GameplaySessionManager manager) =>
        manager.GetDiagnostics().TotalCommittedEvents;

    [Fact]
    public async Task No_trusted_record_stays_unresolved_without_local_character_evidence()
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
                    "2026-08-04 06:27:10 New Hero hits you with their effect.",
                    contextId,
                    source),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 structurally timestamped",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session =>
                    session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Unresolved));

            var session = manager.Current.Sessions[0];
            Assert.Equal(CharacterIdentityConfidence.Unknown, session.CharacterIdentityConfidence);
            Assert.Equal(0, session.CandidateCount);
            Assert.Null(repository.TryFindTrustedByDisplayName("acct-1", "New Hero"));
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
    public async Task Clear_identity_preserves_repository_trust()
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
                    source)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.CharacterRecordId is not null));

            var recordBefore = repository.TryFindTrustedByDisplayName("acct-1", "Example Hero")!;
            var result = manager.ClearIdentity(contextId);
            Assert.True(result.IsSuccess);

            var session = manager.Current.Sessions[0];
            Assert.Null(session.CharacterRecordId);
            Assert.Equal(CharacterIdentityConfidence.Unknown, session.CharacterIdentityConfidence);
            Assert.Equal(CharacterIdentityResolutionState.Unresolved, session.CharacterIdentityResolutionState);

            var recordAfter = repository.TryGetRecord(recordBefore.RecordId)!;
            Assert.Equal(CharacterTrustState.TrustedFromWelcome, recordAfter.TrustState);
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
    public async Task Manual_confirmation_establishes_trust_separately_from_runtime_axes()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var invalidations = 0L;

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
            manager.StateChanged += (_, _) => Interlocked.Increment(ref invalidations);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 structurally timestamped",
                    contextId,
                    source)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.RetainedEventCount == 1));
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);

            var establish = repository.EstablishTrustedFromManualConfirmation("acct-1", "New Hero");
            Interlocked.Exchange(ref invalidations, 0);
            var confirm = manager.ConfirmCharacter(contextId, establish.RecordId!);
            Assert.True(confirm.IsSuccess);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);

            var session = manager.Current.Sessions.Single(item => item.ContextId == contextId);
            Assert.Equal(CharacterIdentityConfidence.Confirmed, session.CharacterIdentityConfidence);
            Assert.Equal(CharacterIdentityResolutionState.Resolved, session.CharacterIdentityResolutionState);
            Assert.Equal(establish.RecordId, session.CharacterRecordId);
            Assert.Equal(1, Volatile.Read(ref invalidations));

            var record = repository.TryGetRecord(establish.RecordId!)!;
            Assert.Equal(CharacterTrustState.TrustedFromManualConfirmation, record.TrustState);
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
    public async Task Clear_identity_raises_state_changed_once()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var invalidations = 0L;
        var identityResolved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

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
            manager.StateChanged += (_, args) =>
            {
                Interlocked.Increment(ref invalidations);
                if (args.Snapshot.Sessions.Any(session => session.CharacterRecordId is not null))
                {
                    identityResolved.TrySetResult();
                }
            };

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source)
            ]);

            await identityResolved.Task;

            Interlocked.Exchange(ref invalidations, 0);
            var result = manager.ClearIdentity(contextId);
            Assert.True(result.IsSuccess);
            Assert.Equal(1, Volatile.Read(ref invalidations));
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

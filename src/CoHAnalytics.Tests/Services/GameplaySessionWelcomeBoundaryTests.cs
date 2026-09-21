using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionWelcomeBoundaryTests
{
    [Fact]
    public async Task Welcome_starts_confirmed_session_and_finalizes_prior_provisional_session()
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
                    "2026-08-04 06:27:09 structurally timestamped",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => committed.Any(event_ => event_.ParserEvent.Sequence == 2));

            var session = manager.Current.Sessions[0];
            Assert.Equal(CharacterIdentityResolutionState.Resolved, session.CharacterIdentityResolutionState);
            Assert.NotNull(repository.TryFindTrustedByDisplayName("acct-1", "Example Hero"));
            Assert.Contains(committed, event_ => event_.ParserEvent.Sequence == 2);
            Assert.DoesNotContain(committed, event_ => event_.ParserEvent.Sequence == 1);
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
    public async Task Same_character_welcome_preserves_session_identity_and_accumulated_state()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var firstWelcomeAt = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var repeatedWelcomeAt = firstWelcomeAt.AddMinutes(1);
        var time = new ManualTimeProvider(firstWelcomeAt);

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
                    observedAt: firstWelcomeAt)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.CharacterRecordId is not null
                    && session.CharacterDisplayName == "Example Hero"));

            var startTracked = manager.StartTrackedCombat(contextId);
            Assert.True(startTracked.IsSuccess);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:01 You gain 900 experience and 100 influence.",
                    contextId,
                    source,
                    sequence: 2,
                    observedAt: firstWelcomeAt.AddSeconds(1)),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:02 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
                    contextId,
                    source,
                    sequence: 3,
                    observedAt: firstWelcomeAt.AddSeconds(2))
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.SessionExperienceGained == 900
                    && session.SessionGameplayInfluenceGained == 100
                    && session.Combat.DamageDealt == new CombatScaledAmount(1388)
                    && session.Combat.Tracked.IsTracking
                    && session.Combat.Tracked.DamageDealt == new CombatScaledAmount(1388)));

            var beforeRepeat = Assert.Single(manager.Current.Sessions);
            var characterRecordId = Assert.IsType<CharacterRecordId>(beforeRepeat.CharacterRecordId);
            time.Advance(TimeSpan.FromMinutes(1));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:01:00 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 4,
                    observedAt: repeatedWelcomeAt)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                repository.TryGetRecord(characterRecordId)?.LastObservedAt == repeatedWelcomeAt);

            var afterRepeat = Assert.Single(manager.Current.Sessions);
            Assert.Equal(beforeRepeat.SessionId, afterRepeat.SessionId);
            Assert.Equal(beforeRepeat.StartedAt, afterRepeat.StartedAt);
            Assert.Equal(characterRecordId, afterRepeat.CharacterRecordId);
            Assert.Equal("Example Hero", afterRepeat.CharacterDisplayName);
            Assert.Equal(900, afterRepeat.SessionExperienceGained);
            Assert.Equal(100, afterRepeat.SessionGameplayInfluenceGained);
            Assert.Equal(new CombatScaledAmount(1388), afterRepeat.Combat.DamageDealt);
            Assert.True(afterRepeat.Combat.Tracked.IsTracking);
            Assert.Equal(new CombatScaledAmount(1388), afterRepeat.Combat.Tracked.DamageDealt);

            var trustedRecord = Assert.Single(repository.Current.Records);
            Assert.Equal(characterRecordId, trustedRecord.RecordId);
            Assert.Equal(CharacterTrustState.TrustedFromWelcome, trustedRecord.TrustState);
            Assert.Equal(repeatedWelcomeAt, trustedRecord.LastObservedAt);
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

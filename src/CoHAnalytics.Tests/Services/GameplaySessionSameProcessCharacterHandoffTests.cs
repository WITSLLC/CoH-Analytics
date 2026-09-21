using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionSameProcessCharacterHandoffTests
{
    private static readonly DateTimeOffset StartA = new(2026, 8, 14, 1, 15, 39, TimeSpan.Zero);
    private static readonly string ExecutablePath =
        @"C:\Games\Homecoming\bin\win64\live\cityofheroes.exe";

    [Fact]
    public async Task Same_process_same_account_welcome_handoff_ends_old_character_and_starts_fresh_session()
    {
        var processInstance = CreateClient(3_524, StartA);
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var (monitoring, _, logActivity, _, gameplay, identity, _, parser) =
                await CreateCharacterHandoffStackAsync(processInstance, repository);

            var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
            logActivity.Current = TestLogCandidates.Snapshot(
                sessionStart,
                TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
            logActivity.RaiseActivityChanged();

            var context = Assert.Single(monitoring.Current.Contexts);
            Assert.Equal(processInstance, context.ProcessInstance);
            var contextId = context.ContextId;

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    contextId,
                    primarySource,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:01 You gain 900 experience and 100 influence.",
                    contextId,
                    primarySource,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => gameplay.Current.Sessions.Any(session =>
                    session.SessionExperienceGained == 900
                    && string.Equals(session.CharacterDisplayName, "Dawn's Vanguard", StringComparison.Ordinal)));

            var hellsSessionId = Assert.Single(gameplay.Current.Sessions).SessionId;

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:05:00 Welcome to City of Heroes, Alpha Hero!",
                    contextId,
                    primarySource,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:05:01 You gain 50 experience.",
                    contextId,
                    primarySource,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => gameplay.Current.Sessions.Any(session =>
                    string.Equals(session.CharacterDisplayName, "Alpha Hero", StringComparison.Ordinal)
                    && session.SessionExperienceGained == 50
                    && session.SessionGameplayInfluenceGained == 0));

            var primarySession = Assert.Single(gameplay.Current.Sessions);
            Assert.Equal(contextId, primarySession.ContextId);
            Assert.Equal("TestAccount", primarySession.AccountStableId);
            Assert.Equal(processInstance, Assert.Single(monitoring.Current.Contexts).ProcessInstance);
            Assert.NotEqual(hellsSessionId, primarySession.SessionId);
            Assert.Equal(
                "Alpha Hero",
                identity.Current.Contexts.Single(context => context.ContextId == contextId).CharacterDisplayName);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Repeated_welcome_for_same_character_does_not_reset_live_session()
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
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 You gain 900 experience.",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.SessionExperienceGained == 900));

            var originalSessionId = manager.Current.Sessions[0].SessionId;

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:28:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    contextId,
                    source,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:28:01 You gain 50 experience.",
                    contextId,
                    source,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.SessionExperienceGained == 950));

            var session = Assert.Single(manager.Current.Sessions);
            Assert.Equal(originalSessionId, session.SessionId);
            Assert.Equal("Dawn's Vanguard", session.CharacterDisplayName);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Manual_confirmation_of_different_character_starts_fresh_live_session()
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

            repository.EstablishTrustedFromManualConfirmation("TestAccount", "Alpha Hero");

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 You gain 900 experience.",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.SessionExperienceGained == 900));

            var hellsSessionId = manager.Current.Sessions[0].SessionId;
            var primaryRecord = repository.TryFindTrustedByDisplayName("TestAccount", "Alpha Hero")!;
            var confirm = manager.ConfirmCharacter(contextId, primaryRecord.RecordId);
            Assert.True(confirm.IsSuccess);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session =>
                    string.Equals(session.CharacterDisplayName, "Alpha Hero", StringComparison.Ordinal)));

            var session = Assert.Single(manager.Current.Sessions);
            Assert.NotEqual(hellsSessionId, session.SessionId);
            Assert.Equal(0, session.SessionExperienceGained);
            Assert.Equal("Alpha Hero", session.CharacterDisplayName);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Ambiguous_character_evidence_does_not_handoff_from_resolved_character()
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

            repository.EstablishTrustedFromWelcome("TestAccount", "Dawn's Vanguard");
            repository.EstablishTrustedFromWelcome("TestAccount", "Alpha Hero");

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 Alpha Hero hits you with their effect.",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session =>
                    session.CharacterIdentityConfidence == CharacterIdentityConfidence.Confirmed));

            var session = Assert.Single(manager.Current.Sessions);
            Assert.Equal("Dawn's Vanguard", session.CharacterDisplayName);
            Assert.Equal(CharacterIdentityResolutionState.Resolved, session.CharacterIdentityResolutionState);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Pre_identity_events_commit_to_new_character_after_welcome_handoff()
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
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 You gain 900 experience.",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.SessionExperienceGained == 900));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:28:00 Welcome to City of Heroes, Alpha Hero!",
                    contextId,
                    source,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:28:01 You gain 75 experience.",
                    contextId,
                    source,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session =>
                    string.Equals(session.CharacterDisplayName, "Alpha Hero", StringComparison.Ordinal)
                    && session.SessionExperienceGained == 75));

            Assert.DoesNotContain(
                manager.Current.Sessions,
                session => string.Equals(session.CharacterDisplayName, "Dawn's Vanguard", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Character_handoff_does_not_reset_other_client_live_sessions()
    {
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var (monitoring, runtime, logActivity, _, gameplay, _, _, parser) =
                await CreateCharacterHandoffStackAsync(
                    [CreateClient(3_524, StartA), CreateClient(6_356, StartA.AddHours(1))],
                    repository);

            var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
            var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");

            logActivity.Current = TestLogCandidates.Snapshot(
                sessionStart,
                TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
            logActivity.RaiseActivityChanged();

            var primaryContext = Assert.Single(monitoring.Current.Contexts);

            logActivity.Current = TestLogCandidates.Snapshot(
                revision: 2,
                sessionStart,
                TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart),
                TestLogCandidates.Create(altSource, LogSourceActivityState.Growing, sessionStart));
            runtime.RaiseStatusChanged(
                GameRuntimeStatus.Running,
                GameRuntimeStatus.Running,
                [CreateClient(3_524, StartA), CreateClient(6_356, StartA.AddHours(1))]);
            logActivity.RaiseActivityChanged();

            var altContext = monitoring.Current.Contexts
                .Single(context => string.Equals(context.AccountStableId, "AltAccount", StringComparison.Ordinal));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    primaryContext.ContextId,
                    primarySource,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:01 You gain 500 experience.",
                    primaryContext.ContextId,
                    primarySource,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, D4wn's Vanguard!",
                    altContext.ContextId,
                    altSource,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:01 You gain 300 experience.",
                    altContext.ContextId,
                    altSource,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                gameplay.Current.Sessions.Count == 2
                && gameplay.Current.Sessions.All(session =>
                    session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Resolved));

            var altSessionId = gameplay.Current.Sessions
                .Single(session => session.ContextId == altContext.ContextId)
                .SessionId;

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:05:00 Welcome to City of Heroes, Alpha Hero!",
                    primaryContext.ContextId,
                    primarySource,
                    sequence: 5)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => gameplay.Current.Sessions.Any(session =>
                    session.ContextId == primaryContext.ContextId
                    && string.Equals(session.CharacterDisplayName, "Alpha Hero", StringComparison.Ordinal)));

            var altSession = gameplay.Current.Sessions
                .Single(session => session.ContextId == altContext.ContextId);
            Assert.Equal(altSessionId, altSession.SessionId);
            Assert.Equal(300, altSession.SessionExperienceGained);
            Assert.Equal("D4wn's Vanguard", altSession.CharacterDisplayName);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Character_handoff_restarts_elapsed_time_for_new_character()
    {
        var processInstance = CreateClient(3_524, StartA);
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var handoffAt = sessionStart.AddMinutes(10);
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var (monitoring, _, logActivity, time, gameplay, _, _, parser) =
                await CreateCharacterHandoffStackAsync(processInstance, repository);

            var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
            logActivity.Current = TestLogCandidates.Snapshot(
                sessionStart,
                TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
            logActivity.RaiseActivityChanged();

            var contextId = Assert.Single(monitoring.Current.Contexts).ContextId;
            time.Set(sessionStart);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    contextId,
                    primarySource,
                    sequence: 1,
                    observedAt: sessionStart)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => gameplay.Current.Sessions.Any(session =>
                    string.Equals(session.CharacterDisplayName, "Dawn's Vanguard", StringComparison.Ordinal)));

            var hellsStartedAt = gameplay.Current.Sessions[0].StartedAt;
            time.Set(handoffAt);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:10:00 Welcome to City of Heroes, Alpha Hero!",
                    contextId,
                    primarySource,
                    sequence: 2,
                    observedAt: handoffAt)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => gameplay.Current.Sessions.Any(session =>
                    string.Equals(session.CharacterDisplayName, "Alpha Hero", StringComparison.Ordinal)));

            var primarySession = Assert.Single(gameplay.Current.Sessions);
            Assert.Equal(handoffAt, primarySession.StartedAt);
            Assert.NotEqual(hellsStartedAt, primarySession.StartedAt);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Character_handoff_does_not_carry_old_rewards_into_new_character()
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
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:01 You received Reward Merit.",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session =>
                    session.RewardCurrencyTotals.Any(total =>
                        total.CurrencyDisplayName == "Reward Merit")));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:28:00 Welcome to City of Heroes, Alpha Hero!",
                    contextId,
                    source,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:28:01 You received Astral Merit.",
                    contextId,
                    source,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session =>
                    string.Equals(session.CharacterDisplayName, "Alpha Hero", StringComparison.Ordinal)
                    && session.RewardCurrencyTotals.Any(total =>
                        total.CurrencyDisplayName == "Astral Merit")));

            var session = Assert.Single(manager.Current.Sessions);
            Assert.DoesNotContain(
                session.RewardCurrencyTotals,
                total => total.CurrencyDisplayName == "Reward Merit");
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Following_live_remains_on_same_context_after_character_handoff()
    {
        var processInstance = CreateClient(3_524, StartA);
        var sessionStart = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var (monitoring, _, logActivity, _, _, identity, viewed, parser) =
                await CreateCharacterHandoffStackAsync(processInstance, repository);

            var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
            logActivity.Current = TestLogCandidates.Snapshot(
                sessionStart,
                TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
            logActivity.RaiseActivityChanged();

            var contextId = Assert.Single(monitoring.Current.Contexts).ContextId;

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    contextId,
                    primarySource,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => identity.Current.Contexts.Any(context =>
                    string.Equals(context.CharacterDisplayName, "Dawn's Vanguard", StringComparison.Ordinal)));

            viewed.SelectGameplaySessionContext(contextId);
            Assert.Equal(contextId, viewed.Current.LiveFollowContextId);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:05:00 Welcome to City of Heroes, Alpha Hero!",
                    contextId,
                    primarySource,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => identity.Current.Contexts.Any(context =>
                    string.Equals(context.CharacterDisplayName, "Alpha Hero", StringComparison.Ordinal)));

            Assert.Equal(contextId, viewed.Current.LiveFollowContextId);
            Assert.Equal(
                "Alpha Hero",
                identity.Current.Contexts.Single(context => context.ContextId == contextId).CharacterDisplayName);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    private static HomecomingProcessInstance CreateClient(int processId, DateTimeOffset processStartTime) =>
        FakeGameRuntimeService.CreateClient(processId, processStartTime, ExecutablePath);

    private static async Task<(
        MonitoringSessionManager Monitoring,
        FakeGameRuntimeService Runtime,
        FakeLogActivityService LogActivity,
        ManualTimeProvider Time,
        GameplaySessionManager Gameplay,
        GameplaySessionIdentityReadService Identity,
        ViewedContextService Viewed,
        GameplaySessionTestInfrastructure.FakeGameplayParserManager Parser)> CreateCharacterHandoffStackAsync(
        HomecomingProcessInstance processInstance,
        CharacterRepository? repository = null)
    {
        return await CreateCharacterHandoffStackAsync([processInstance], repository);
    }

    private static async Task<(
        MonitoringSessionManager Monitoring,
        FakeGameRuntimeService Runtime,
        FakeLogActivityService LogActivity,
        ManualTimeProvider Time,
        GameplaySessionManager Gameplay,
        GameplaySessionIdentityReadService Identity,
        ViewedContextService Viewed,
        GameplaySessionTestInfrastructure.FakeGameplayParserManager Parser)> CreateCharacterHandoffStackAsync(
        IReadOnlyList<HomecomingProcessInstance> runningClients,
        CharacterRepository? repository = null)
    {
        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClients = runningClients,
            RunningClientCount = runningClients.Count
        };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var monitoring = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = time });
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        repository ??= GameplaySessionTestInfrastructure.CreateRepository(out _);
        var gameplay = new GameplaySessionManager(monitoring, parser, repository, new GameplaySessionOptions
        {
            TimeProvider = time
        });
        var identity = new GameplaySessionIdentityReadService(gameplay, monitoring, repository);
        var viewed = new ViewedContextService(identity, repository);
        _ = new LiveRuntimeGenerationService(runtime, monitoring, gameplay, viewed);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, runningClients);
        await monitoring.StartAsync();
        await gameplay.StartAsync();

        return (monitoring, runtime, logActivity, time, gameplay, identity, viewed, parser);
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

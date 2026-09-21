using System.Text;
using System.Collections.Concurrent;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class HomecomingLifecycleRegressionTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ProvisionalStart =
        new(2026, 8, 16, 11, 59, 55, TimeSpan.Zero);
    private static readonly DateTimeOffset RefinedStart =
        new(2026, 8, 16, 11, 58, 0, TimeSpan.Zero);

    [Fact]
    public async Task Game_first_startup_establishes_ready_session_and_character()
    {
        using var logs = new ParserTestDirectory();
        var source = CreateWelcomeSource(logs, "TestAccount", "Dawn's Vanguard");
        await using var stack = CreateStack(
            TestLogCandidates.Snapshot(
                ObservedAt,
                TestLogCandidates.Create(source, LogSourceActivityState.Growing, ObservedAt)));
        var client = FakeGameRuntimeService.CreateClient(3_524, ProvisionalStart);

        stack.Runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, [client]);
        await stack.StartAsync();

        await AssertResolvedActiveSessionAsync(stack, "TestAccount", "Dawn's Vanguard");
    }

    [Fact]
    public async Task Analytics_first_runtime_then_log_growth_establishes_active_session()
    {
        using var logs = new ParserTestDirectory();
        var source = CreateWelcomeSource(logs, "TestAccount", "Dawn's Vanguard");
        await using var stack = CreateStack(LogActivitySnapshot.Empty);
        await stack.StartAsync();

        stack.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Off,
            GameRuntimeStatus.Running,
            [FakeGameRuntimeService.CreateClient(3_524, ProvisionalStart)]);
        stack.LogActivity.Current = TestLogCandidates.Snapshot(
            ObservedAt,
            TestLogCandidates.Create(source, LogSourceActivityState.Growing, ObservedAt));
        stack.LogActivity.RaiseActivityChanged();

        await AssertResolvedActiveSessionAsync(stack, "TestAccount", "Dawn's Vanguard");
    }

    [Fact]
    public async Task Analytics_first_log_growth_before_runtime_poll_establishes_active_session()
    {
        using var logs = new ParserTestDirectory();
        var source = CreateWelcomeSource(logs, "TestAccount", "Dawn's Vanguard");
        await using var stack = CreateStack(LogActivitySnapshot.Empty);
        await stack.StartAsync();

        stack.LogActivity.Current = TestLogCandidates.Snapshot(
            ObservedAt,
            TestLogCandidates.Create(source, LogSourceActivityState.Growing, ObservedAt));
        stack.LogActivity.RaiseActivityChanged();
        Assert.Empty(stack.Monitoring.Current.Contexts);

        stack.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Off,
            GameRuntimeStatus.Running,
            [FakeGameRuntimeService.CreateClient(3_524, ProvisionalStart)]);

        await AssertResolvedActiveSessionAsync(stack, "TestAccount", "Dawn's Vanguard");
    }

    [Fact]
    public async Task Same_pid_metadata_refinement_preserves_context_session_identity_and_telemetry()
    {
        using var logs = new ParserTestDirectory();
        var source = CreateWelcomeSource(logs, "TestAccount", "Dawn's Vanguard");
        await using var stack = CreateStack(
            TestLogCandidates.Snapshot(
                ObservedAt,
                TestLogCandidates.Create(source, LogSourceActivityState.Growing, ObservedAt)));
        var provisionalClient = FakeGameRuntimeService.CreateClient(3_524, ProvisionalStart);
        var refinedClient = FakeGameRuntimeService.CreateClient(3_524, RefinedStart);

        stack.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Off,
            GameRuntimeStatus.Running,
            [provisionalClient]);
        await stack.StartAsync();
        await AssertResolvedActiveSessionAsync(stack, "TestAccount", "Dawn's Vanguard");

        var contextId = Assert.Single(stack.Monitoring.Current.Contexts).ContextId;
        stack.Parser.PublishClassified([
            GameplaySessionTestInfrastructure.Classify(
                "2026-08-16 12:00:01 You gain 321 experience.",
                contextId,
                source,
                sequence: 2)
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(
            () => stack.Gameplay.Current.Sessions.Single().SessionExperienceGained == 321);

        var originalSessionId = Assert.Single(stack.Gameplay.Current.Sessions).SessionId;
        var trackedCombatPublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        bool IsTrackedCombatPublished(GameplaySessionManagerSnapshot snapshot) =>
            snapshot.Sessions.Any(session =>
                session.ContextId == contextId
                && session.SessionId == originalSessionId
                && session.Combat.Tracked.IsTracking);

        EventHandler<GameplaySessionManagerChangedEventArgs> trackedCombatHandler = (_, args) =>
        {
            if (IsTrackedCombatPublished(args.Snapshot))
            {
                trackedCombatPublished.TrySetResult();
            }
        };

        stack.Gameplay.StateChanged += trackedCombatHandler;
        try
        {
            Assert.True(stack.Gameplay.StartTrackedCombat(contextId).IsSuccess);
            if (IsTrackedCombatPublished(stack.Gameplay.Current))
            {
                trackedCombatPublished.TrySetResult();
            }

            await trackedCombatPublished.Task;
        }
        finally
        {
            stack.Gameplay.StateChanged -= trackedCombatHandler;
        }

        var originalSession = Assert.Single(stack.Gameplay.Current.Sessions);
        Assert.True(originalSession.Combat.Tracked.IsTracking);

        stack.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [refinedClient]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            stack.Monitoring.Current.Contexts.Single().ProcessInstance == refinedClient
            && stack.Gameplay.GetDiagnostics().WorkQueue.IsDrained);

        var refinedContext = Assert.Single(stack.Monitoring.Current.Contexts);
        var refinedSession = Assert.Single(stack.Gameplay.Current.Sessions);
        Assert.Equal(contextId, refinedContext.ContextId);
        Assert.Equal(originalSession.SessionId, refinedSession.SessionId);
        Assert.Equal(originalSession.CharacterRecordId, refinedSession.CharacterRecordId);
        Assert.Equal(321, refinedSession.SessionExperienceGained);
        Assert.True(refinedSession.Combat.Tracked.IsTracking);
    }

    [Fact]
    public async Task Full_exit_and_relaunch_creates_new_ready_session_without_parser_wakeup()
    {
        using var logs = new ParserTestDirectory();
        var sourceA = CreateWelcomeSource(logs, "TestAccount", "Dawn's Vanguard", "chatlog-a.txt");
        var sourceB = CreateWelcomeSource(logs, "AltAccount", "D4wn's Vanguard", "chatlog-b.txt");
        await using var stack = CreateStack(LogActivitySnapshot.Empty);
        await stack.StartAsync();

        stack.LogActivity.Current = TestLogCandidates.Snapshot(
            ObservedAt,
            TestLogCandidates.Create(sourceA, LogSourceActivityState.Growing, ObservedAt));
        stack.LogActivity.RaiseActivityChanged();
        var clientA = FakeGameRuntimeService.CreateClient(3_524, ProvisionalStart);
        stack.Runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, [clientA]);
        await AssertResolvedActiveSessionAsync(stack, "TestAccount", "Dawn's Vanguard");
        var sessionA = Assert.Single(stack.Gameplay.Current.Sessions);

        stack.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            stack.Gameplay.Current.Sessions.Single().LifecycleState == GameplaySessionLifecycleState.Suspended
            && stack.Identity.Current.Contexts.Single().SessionLifecycleState
                == GameplaySessionLifecycleState.Suspended);
        var historicalA = Assert.Single(stack.Identity.Current.Contexts);
        Assert.Equal(GameplaySessionLifecycleState.Suspended, historicalA.SessionLifecycleState);
        Assert.NotNull(historicalA.SessionTimingEndAt);

        var relaunchAt = ObservedAt.AddMinutes(5);
        stack.Time.Set(relaunchAt);
        stack.LogActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            relaunchAt,
            TestLogCandidates.Create(
                sourceA,
                LogSourceActivityState.Growing,
                relaunchAt,
                lastGrowthAt: ObservedAt),
            TestLogCandidates.Create(
                sourceB,
                LogSourceActivityState.Growing,
                relaunchAt,
                lastGrowthAt: relaunchAt));
        stack.LogActivity.RaiseActivityChanged();
        stack.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Off,
            GameRuntimeStatus.Running,
            [FakeGameRuntimeService.CreateClient(6_356, relaunchAt)]);

        var liveContext = Assert.Single(stack.Monitoring.Current.Contexts);
        Assert.Equal("AltAccount", liveContext.AccountStableId);
        Assert.Equal(MonitoringContextState.Ready, liveContext.State);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(
            () => stack.Gameplay.GetDiagnostics().WorkQueue.IsDrained);
        var sessionB = Assert.Single(stack.Gameplay.Current.Sessions);
        Assert.Equal("AltAccount", sessionB.AccountStableId);
        Assert.Equal(GameplaySessionLifecycleState.Active, sessionB.LifecycleState);
        Assert.Equal("D4wn's Vanguard", sessionB.CharacterDisplayName);
        Assert.NotEqual(sessionA.SessionId, sessionB.SessionId);
        Assert.Equal(0, sessionB.SessionExperienceGained);
    }

    [Fact]
    public async Task Same_account_full_relaunch_identity_matrix_is_generation_safe()
    {
        using var logs = new ParserTestDirectory();
        var source = CreateWelcomeSource(logs, "TestAccount", "Scout");
        var initialLength = new FileInfo(source.FilePath).Length;
        await using var stack = CreateStack(TestLogCandidates.Snapshot(
            ObservedAt,
            TestLogCandidates.Create(
                source,
                LogSourceActivityState.Growing,
                ObservedAt,
                length: initialLength,
                previousLength: 0)));
        var committed = new ConcurrentQueue<GameplaySessionEvent>();
        stack.Gameplay.CommittedEventsAvailable += (_, args) =>
        {
            foreach (var gameplayEvent in args.Events)
            {
                committed.Enqueue(gameplayEvent);
            }
        };

        stack.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Off,
            GameRuntimeStatus.Running,
            [FakeGameRuntimeService.CreateClient(3_524, ProvisionalStart)]);
        await stack.StartAsync();
        await AssertResolvedActiveSessionAsync(stack, "TestAccount", "Scout");
        var originalContext = Assert.Single(stack.Monitoring.Current.Contexts);
        var originalSession = Assert.Single(stack.Gameplay.Current.Sessions);
        var scoutRecordId = originalSession.CharacterRecordId;

        // A: same account, same character. A current-generation Welcome must create a fresh
        // context/session while resolving back to Scout's persistent character record.
        var sameCharacterAt = ObservedAt.AddMinutes(5);
        await ExitAndPrepareRelaunchAsync(stack, source, sameCharacterAt, revision: 2);
        AppendLine(source, "[04:02] Welcome to City of Heroes, Scout!\r\n");
        PublishGrowingSource(stack, source, sameCharacterAt, revision: 3);
        stack.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Off,
            GameRuntimeStatus.Running,
            [FakeGameRuntimeService.CreateClient(6_356, sameCharacterAt)]);
        await AssertResolvedActiveSessionAsync(stack, "TestAccount", "Scout");
        var sameCharacterContext = Assert.Single(stack.Monitoring.Current.Contexts);
        var sameCharacterSession = Assert.Single(stack.Gameplay.Current.Sessions);
        Assert.NotEqual(originalContext.ContextId, sameCharacterContext.ContextId);
        Assert.NotEqual(originalSession.SessionId, sameCharacterSession.SessionId);
        Assert.Equal(scoutRecordId, sameCharacterSession.CharacterRecordId);

        // B: same account, different character. Telemetry committed after recovery must carry
        // only Dawn's Vanguard's session and character identifiers.
        var differentCharacterAt = sameCharacterAt.AddMinutes(5);
        await ExitAndPrepareRelaunchAsync(stack, source, differentCharacterAt, revision: 4);
        AppendLine(source, "[04:07] Welcome to City of Heroes, Dawn's Vanguard!\r\n");
        PublishGrowingSource(stack, source, differentCharacterAt, revision: 5);
        stack.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Off,
            GameRuntimeStatus.Running,
            [FakeGameRuntimeService.CreateClient(7_421, differentCharacterAt)]);
        await AssertResolvedActiveSessionAsync(stack, "TestAccount", "Dawn's Vanguard");
        var hellContext = Assert.Single(stack.Monitoring.Current.Contexts);
        var primaryHeroSession = Assert.Single(stack.Gameplay.Current.Sessions);
        Assert.NotEqual(sameCharacterContext.ContextId, hellContext.ContextId);
        Assert.NotEqual(sameCharacterSession.SessionId, primaryHeroSession.SessionId);
        Assert.NotEqual(scoutRecordId, primaryHeroSession.CharacterRecordId);

        while (committed.TryDequeue(out _))
        {
        }

        var telemetryLines = new[]
        {
            "[04:08] You gain 900 experience and 100 influence.",
            "[04:09] You received Reward Merit."
        };
        stack.Parser.PublishClassified([
            GameplaySessionTestInfrastructure.Classify(
                telemetryLines[0], hellContext.ContextId, source, sequence: 2),
            GameplaySessionTestInfrastructure.Classify(
                telemetryLines[1], hellContext.ContextId, source, sequence: 3)
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            stack.Gameplay.Current.Sessions.Single().SessionExperienceGained == 900
            && stack.Gameplay.Current.Sessions.Single().SessionGameplayInfluenceGained == 100
            && stack.Gameplay.Current.Sessions.Single().RecentRewards.Any(entry =>
                entry.DisplayName == "Reward Merit"));
        var hellTelemetry = committed
            .Where(gameplayEvent => telemetryLines.Contains(gameplayEvent.ParserEvent.RawLine))
            .ToArray();
        Assert.Equal(2, hellTelemetry.Length);
        Assert.All(hellTelemetry, gameplayEvent =>
        {
            Assert.Equal(primaryHeroSession.SessionId, gameplayEvent.SessionId);
            Assert.Equal(primaryHeroSession.CharacterRecordId, gameplayEvent.CharacterRecordId);
            Assert.NotEqual(scoutRecordId, gameplayEvent.CharacterRecordId);
        });

        // C: round-trip back to Scout must create another context/session and resolve the
        // original Scout record, never reuse Hell's live identity.
        var roundTripAt = differentCharacterAt.AddMinutes(5);
        PublishGrowingSource(stack, source, roundTripAt, revision: 6);
        await ExitAndPrepareRelaunchAsync(stack, source, roundTripAt, revision: 7);
        AppendLine(source, "[04:12] Welcome to City of Heroes, Scout!\r\n");
        PublishGrowingSource(stack, source, roundTripAt, revision: 8);
        stack.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Off,
            GameRuntimeStatus.Running,
            [FakeGameRuntimeService.CreateClient(8_105, roundTripAt)]);
        await AssertResolvedActiveSessionAsync(stack, "TestAccount", "Scout");
        var roundTripContext = Assert.Single(stack.Monitoring.Current.Contexts);
        var roundTripSession = Assert.Single(stack.Gameplay.Current.Sessions);
        Assert.NotEqual(hellContext.ContextId, roundTripContext.ContextId);
        Assert.NotEqual(primaryHeroSession.SessionId, roundTripSession.SessionId);
        Assert.Equal(scoutRecordId, roundTripSession.CharacterRecordId);

        // D: activity without a current-generation Welcome must not inherit Scout. The startup
        // scan and its single activity-triggered retry both encounter the historical Welcome,
        // reject it at the runtime boundary, and leave manual selection available.
        var noWelcomeAt = roundTripAt.AddMinutes(5);
        await ExitAndPrepareRelaunchAsync(stack, source, noWelcomeAt, revision: 9);
        AppendLine(source, "[04:17] You gain 1 experience.\r\n");
        PublishGrowingSource(stack, source, noWelcomeAt, revision: 10);
        stack.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Off,
            GameRuntimeStatus.Running,
            [FakeGameRuntimeService.CreateClient(9_009, noWelcomeAt)]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            stack.Gameplay.Current.Sessions.Any(session =>
                session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Unresolved));
        var unknownContext = Assert.Single(stack.Monitoring.Current.Contexts);
        stack.Parser.PublishClassified([
            GameplaySessionTestInfrastructure.Classify(
                "[04:18] You gain 2 experience.", unknownContext.ContextId, source, sequence: 2)
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            stack.Gameplay.GetDiagnostics().WorkQueue.IsDrained);
        var unknownSession = Assert.Single(stack.Gameplay.Current.Sessions);
        var unknownIdentity = Assert.Single(stack.Identity.Current.Contexts);
        Assert.Null(unknownSession.CharacterRecordId);
        Assert.Equal(
            CharacterIdentityResolutionState.Unresolved,
            unknownSession.CharacterIdentityResolutionState);
        Assert.True(unknownIdentity.RequiresManualSelection);
        Assert.Contains(
            stack.Gameplay.GetDiagnostics().RecentOperations,
            operation => operation.Contains("Provisional session started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_clients_to_one_preserves_process_proven_survivor_despite_log_recency()
    {
        using var logs = new ParserTestDirectory();
        var sourceA = CreateWelcomeSource(logs, "TestAccount", "Dawn's Vanguard", "chatlog-a.txt");
        var sourceB = CreateWelcomeSource(logs, "AltAccount", "D4wn's Vanguard", "chatlog-b.txt");
        var clientA = FakeGameRuntimeService.CreateClient(3_524, ProvisionalStart);
        var clientB = FakeGameRuntimeService.CreateClient(6_356, ProvisionalStart.AddSeconds(1));
        await using var stack = CreateStack(
            TestLogCandidates.Snapshot(
                ObservedAt,
                TestLogCandidates.Create(sourceA, LogSourceActivityState.Growing, ObservedAt)));
        stack.Runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, [clientA]);
        await stack.StartAsync();
        await AssertResolvedActiveSessionAsync(stack, "TestAccount", "Dawn's Vanguard");

        stack.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [clientA, clientB]);
        stack.LogActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            ObservedAt,
            TestLogCandidates.Create(sourceA, LogSourceActivityState.Growing, ObservedAt),
            TestLogCandidates.Create(sourceB, LogSourceActivityState.Growing, ObservedAt));
        stack.LogActivity.RaiseActivityChanged();
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            stack.Gameplay.Current.Sessions.Count == 2
            && stack.Gameplay.Current.Sessions.Any(session => session.AccountStableId == "AltAccount"));
        var sessionB = stack.Gameplay.Current.Sessions.Single(session => session.AccountStableId == "AltAccount");

        var collapseAt = ObservedAt.AddMinutes(1);
        stack.Time.Set(collapseAt);
        stack.LogActivity.Current = TestLogCandidates.Snapshot(
            revision: 3,
            collapseAt,
            TestLogCandidates.Create(
                sourceA,
                LogSourceActivityState.Growing,
                collapseAt,
                lastGrowthAt: collapseAt),
            TestLogCandidates.Create(
                sourceB,
                LogSourceActivityState.Growing,
                collapseAt,
                lastGrowthAt: ObservedAt));
        stack.LogActivity.RaiseActivityChanged();
        stack.Runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [clientB]);

        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            stack.Monitoring.Current.Contexts.Count == 1
            && stack.Gameplay.Current.Sessions.Count == 1);
        var survivingContext = Assert.Single(stack.Monitoring.Current.Contexts);
        var survivingSession = Assert.Single(stack.Gameplay.Current.Sessions);
        Assert.Equal("AltAccount", survivingContext.AccountStableId);
        Assert.Equal(clientB, survivingContext.ProcessInstance);
        Assert.Equal(sessionB.SessionId, survivingSession.SessionId);
        Assert.Equal("D4wn's Vanguard", survivingSession.CharacterDisplayName);
    }

    [Fact]
    public void Ready_context_without_gameplay_session_is_not_presented_as_finalized()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var contextId = MonitoringContextId.CreateNew();
        var source = GameplaySessionTestInfrastructure.DefaultSource();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            using var gameplay = new GameplaySessionManager(monitoring, parser, repository);
            using var identity = new GameplaySessionIdentityReadService(gameplay, monitoring, repository);

            var context = Assert.Single(identity.Current.Contexts);
            Assert.Null(context.SessionLifecycleState);
            Assert.False(context.HasActiveSession);
            Assert.Equal("No Active Session", context.IdentityStatusLabel);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    private static LogSourceId CreateWelcomeSource(
        ParserTestDirectory logs,
        string account,
        string character,
        string fileName = "chatlog.txt")
    {
        var path = logs.CreateFile(
            fileName,
            Encoding.UTF8.GetBytes($"[03:57] Welcome to City of Heroes, {character}!\r\n"));
        return ParserTestSnapshots.Source(path, account);
    }

    private static void AppendLine(LogSourceId source, string line) =>
        File.AppendAllText(source.FilePath, line, Encoding.UTF8);

    private static void PublishGrowingSource(
        LifecycleStack stack,
        LogSourceId source,
        DateTimeOffset observedAt,
        long revision)
    {
        var length = new FileInfo(source.FilePath).Length;
        stack.Time.Set(observedAt);
        stack.LogActivity.Current = TestLogCandidates.Snapshot(
            revision,
            observedAt,
            TestLogCandidates.Create(
                source,
                LogSourceActivityState.Growing,
                observedAt,
                length: length,
                previousLength: Math.Max(0, length - 1)));
        stack.LogActivity.RaiseActivityChanged();
    }

    private static async Task ExitAndPrepareRelaunchAsync(
        LifecycleStack stack,
        LogSourceId source,
        DateTimeOffset observedAt,
        long revision)
    {
        PublishGrowingSource(stack, source, observedAt, revision);
        stack.Runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            stack.Gameplay.Current.Sessions.Single().LifecycleState
                == GameplaySessionLifecycleState.Suspended);
    }

    private static async Task AssertResolvedActiveSessionAsync(
        LifecycleStack stack,
        string account,
        string character)
    {
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            stack.Monitoring.Current.Contexts.Any(context =>
                context.State == MonitoringContextState.Ready
                && context.AccountStableId == account)
            && stack.Gameplay.Current.Sessions.Any(session =>
                session.AccountStableId == account
                && session.LifecycleState == GameplaySessionLifecycleState.Active
                && session.CharacterDisplayName == character));
    }

    private static LifecycleStack CreateStack(LogActivitySnapshot initialLogActivity) =>
        new(initialLogActivity, ObservedAt);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class LifecycleStack : IAsyncDisposable
    {
        private readonly string _repositoryDirectory;

        public LifecycleStack(LogActivitySnapshot initialLogActivity, DateTimeOffset now)
        {
            Runtime = new FakeGameRuntimeService
            {
                CurrentStatus = GameRuntimeStatus.Off,
                RunningClients = [],
                RunningClientCount = 0
            };
            LogActivity = new FakeLogActivityService { Current = initialLogActivity };
            Time = new ManualTimeProvider(now);
            Monitoring = new MonitoringSessionManager(
                Runtime,
                LogActivity,
                new MonitoringSessionManagerOptions { TimeProvider = Time });
            Parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
            var repository = GameplaySessionTestInfrastructure.CreateRepository(out _repositoryDirectory);
            Gameplay = new GameplaySessionManager(Monitoring, Parser, repository);
            Identity = new GameplaySessionIdentityReadService(Gameplay, Monitoring, repository);
            Viewed = new ViewedContextService(Identity, repository);
            Generation = new LiveRuntimeGenerationService(Runtime, Monitoring, Gameplay, Viewed);
        }

        public FakeGameRuntimeService Runtime { get; }

        public FakeLogActivityService LogActivity { get; }

        public ManualTimeProvider Time { get; }

        public MonitoringSessionManager Monitoring { get; }

        public GameplaySessionTestInfrastructure.FakeGameplayParserManager Parser { get; }

        public GameplaySessionManager Gameplay { get; }

        public GameplaySessionIdentityReadService Identity { get; }

        public ViewedContextService Viewed { get; }

        public LiveRuntimeGenerationService Generation { get; }

        public async Task StartAsync()
        {
            await Monitoring.StartAsync();
            await Gameplay.StartAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Gameplay.StopAsync();
            await Monitoring.StopAsync();
            Generation.Dispose();
            Identity.Dispose();
            Gameplay.Dispose();
            Monitoring.Dispose();
            TryDeleteDirectory(_repositoryDirectory);
        }
    }
}

using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class MonitoringSessionRuntimeGenerationResurrectionTests
{
    [Fact]
    public async Task Post_reset_reconcile_does_not_recreate_stale_TestAccount_when_AltAccount_launches()
    {
        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClientCount = 1
        };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var gameplay = new RecordingGameplaySessionManager();

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");
        var sessionStart = time.GetUtcNow();
        var sessionEnd = sessionStart.AddMinutes(3);

        logActivity.Current = TestLogCandidates.Snapshot(
            sessionStart,
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));

        using var monitoring = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = time });
        using var generation = new LiveRuntimeGenerationService(
            runtime,
            monitoring,
            gameplay,
            new RecordingViewedContextService());

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, runningClientCount: 1);
        await monitoring.StartAsync();
        var primaryContext = Assert.Single(monitoring.Current.Contexts);
        Assert.Equal("TestAccount", primaryContext.AccountStableId);

        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            sessionEnd,
            TestLogCandidates.Create(
                primarySource,
                LogSourceActivityState.Growing,
                sessionEnd,
                lastGrowthAt: sessionEnd));
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, runningClientCount: 0);
        logActivity.RaiseActivityChanged();

        Assert.Equal(MonitoringContextState.RuntimeSuspended, monitoring.Current.Contexts[0].State);

        var relaunchAt = sessionEnd.AddMinutes(1);
        time.Set(relaunchAt);
        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 3,
            relaunchAt,
            TestLogCandidates.Create(
                primarySource,
                LogSourceActivityState.Growing,
                relaunchAt,
                lastGrowthAt: sessionEnd),
            TestLogCandidates.Create(
                altSource,
                LogSourceActivityState.Growing,
                relaunchAt,
                lastGrowthAt: relaunchAt));
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, runningClientCount: 1);
        logActivity.RaiseActivityChanged();

        Assert.Equal(1, gameplay.ResetCount);
        var altContext = Assert.Single(monitoring.Current.Contexts);
        Assert.Equal("AltAccount", altContext.AccountStableId);
        Assert.DoesNotContain(
            monitoring.Current.Contexts,
            context => string.Equals(context.AccountStableId, "TestAccount", StringComparison.Ordinal));
        Assert.NotEqual(primaryContext.ContextId, altContext.ContextId);
    }

    [Fact]
    public async Task Simultaneous_second_client_keeps_TestAccount_and_adds_AltAccount_without_reset()
    {
        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClientCount = 1
        };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var gameplay = new RecordingGameplaySessionManager();

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");
        var now = time.GetUtcNow();

        logActivity.Current = TestLogCandidates.Snapshot(
            now,
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, now));

        using var monitoring = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = time });
        using var generation = new LiveRuntimeGenerationService(
            runtime,
            monitoring,
            gameplay,
            new RecordingViewedContextService());

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, runningClientCount: 1);
        await monitoring.StartAsync();
        var primaryContext = Assert.Single(monitoring.Current.Contexts);

        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            now,
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, now),
            TestLogCandidates.Create(altSource, LogSourceActivityState.Growing, now));
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, runningClientCount: 2);
        logActivity.RaiseActivityChanged();

        Assert.Equal(0, gameplay.ResetCount);
        Assert.Equal(2, monitoring.Current.Contexts.Count);
        Assert.Contains(monitoring.Current.Contexts, context => context.ContextId == primaryContext.ContextId);
        Assert.Contains(
            monitoring.Current.Contexts,
            context => string.Equals(context.AccountStableId, "AltAccount", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Identity_read_model_excludes_removed_generation_contexts_after_account_switch()
    {
        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClientCount = 1
        };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
            var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");
            var sessionStart = time.GetUtcNow();
            var sessionEnd = sessionStart.AddMinutes(3);
            var relaunchAt = sessionEnd.AddMinutes(1);

            logActivity.Current = TestLogCandidates.Snapshot(
                sessionStart,
                TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));

            using var monitoring = new MonitoringSessionManager(
                runtime,
                logActivity,
                new MonitoringSessionManagerOptions { TimeProvider = time });
            var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
            using var gameplay = new GameplaySessionManager(monitoring, parser, repository);
            using var generation = new LiveRuntimeGenerationService(
                runtime,
                monitoring,
                gameplay,
                new RecordingViewedContextService());
            var identity = new GameplaySessionIdentityReadService(gameplay, monitoring, repository);

            runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, runningClientCount: 1);
            await monitoring.StartAsync();
            await gameplay.StartAsync();

            var primaryContextId = monitoring.Current.Contexts[0].ContextId;
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    primaryContextId,
                    primarySource)
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => identity.Current.Contexts.Any(context =>
                    string.Equals(context.CharacterDisplayName, "Dawn's Vanguard", StringComparison.Ordinal)));

            runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, runningClientCount: 0);
            logActivity.Current = TestLogCandidates.Snapshot(
                revision: 2,
                sessionEnd,
                TestLogCandidates.Create(
                    primarySource,
                    LogSourceActivityState.Growing,
                    sessionEnd,
                    lastGrowthAt: sessionEnd));
            logActivity.RaiseActivityChanged();

            time.Set(relaunchAt);
            logActivity.Current = TestLogCandidates.Snapshot(
                revision: 3,
                relaunchAt,
                TestLogCandidates.Create(
                    primarySource,
                    LogSourceActivityState.Growing,
                    relaunchAt,
                    lastGrowthAt: sessionEnd),
                TestLogCandidates.Create(
                    altSource,
                    LogSourceActivityState.Growing,
                    relaunchAt,
                    lastGrowthAt: relaunchAt));
            runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, runningClientCount: 1);
            logActivity.RaiseActivityChanged();

            var altContextId = Assert.Single(monitoring.Current.Contexts).ContextId;
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:04:00 Welcome to City of Heroes, D4wn's Vanguard!",
                    altContextId,
                    altSource)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => identity.Current.Contexts.Any(context =>
                    string.Equals(context.CharacterDisplayName, "D4wn's Vanguard", StringComparison.Ordinal)));

            Assert.DoesNotContain(
                identity.Current.Contexts,
                context => string.Equals(context.AccountStableId, "TestAccount", StringComparison.Ordinal));
            Assert.DoesNotContain(
                identity.Current.Contexts,
                context => string.Equals(context.CharacterDisplayName, "Dawn's Vanguard", StringComparison.Ordinal));
            Assert.Contains(
                identity.Current.Contexts,
                context => string.Equals(context.AccountStableId, "AltAccount", StringComparison.Ordinal));
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
    public async Task Client_A_brief_B_real_C_reacquires_context_after_post_cutoff_growth()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile(
            "chatlog.txt",
            System.Text.Encoding.UTF8.GetBytes("[03:57] seed\r\n"));
        var sourceA = ParserTestSnapshots.Source(path, accountId: "TestAccount");

        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var gameplay = new RecordingGameplaySessionManager();
        var t0 = time.GetUtcNow();
        var clientA = FakeGameRuntimeService.CreateClient(6_001, t0);
        var clientB = FakeGameRuntimeService.CreateClient(6_002, t0.AddMinutes(10));
        var clientC = FakeGameRuntimeService.CreateClient(6_003, t0.AddMinutes(11));

        logActivity.Current = LogActivitySnapshot.Empty;

        using var monitoring = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = time });
        using var generation = new LiveRuntimeGenerationService(
            runtime,
            monitoring,
            gameplay,
            new RecordingViewedContextService());
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());

        await monitoring.StartAsync();
        await parser.StartAsync();

        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, [clientA]);
        var growthA = time.GetUtcNow();
        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 1,
            growthA,
            TestLogCandidates.Create(
                sourceA,
                LogSourceActivityState.Growing,
                growthA,
                lastGrowthAt: growthA,
                length: new FileInfo(path).Length,
                previousLength: 0));
        logActivity.RaiseActivityChanged();

        Assert.Single(monitoring.Current.Contexts);
        await ParserTestSnapshots.WaitUntilAsync(() => parser.Current.Workers.Count == 1);
        var retiredContextId = monitoring.Current.Contexts[0].ContextId;

        var exitA = growthA.AddMinutes(1);
        time.Set(exitA);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);
        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            exitA,
            TestLogCandidates.Create(
                sourceA,
                LogSourceActivityState.Inactive,
                exitA,
                lastGrowthAt: growthA,
                length: new FileInfo(path).Length,
                previousLength: new FileInfo(path).Length));
        logActivity.RaiseActivityChanged();

        var briefB = exitA.AddSeconds(20);
        time.Set(briefB);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, [clientB]);
        Assert.Equal(1, gameplay.ResetCount);
        Assert.Empty(monitoring.Current.Contexts);
        await ParserTestSnapshots.WaitUntilAsync(() => parser.Current.Workers.Count == 0);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);

        var startC = briefB.AddSeconds(30);
        time.Set(startC);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, [clientC]);
        Assert.Equal(2, gameplay.ResetCount);
        Assert.Empty(monitoring.Current.Contexts);

        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 3,
            startC,
            TestLogCandidates.Create(
                sourceA,
                LogSourceActivityState.Growing,
                startC,
                lastGrowthAt: briefB,
                length: new FileInfo(path).Length,
                previousLength: new FileInfo(path).Length));
        logActivity.RaiseActivityChanged();
        Assert.Empty(monitoring.Current.Contexts);
        Assert.Empty(monitoring.Current.PendingOffers);

        var postGrowth = startC.AddMinutes(1);
        time.Set(postGrowth);
        directory.Append(path, "[03:58] grew\r\n");
        var length = new FileInfo(path).Length;
        var postSnapshot = TestLogCandidates.Snapshot(
            revision: 4,
            postGrowth,
            TestLogCandidates.Create(
                sourceA,
                LogSourceActivityState.Growing,
                postGrowth,
                lastGrowthAt: postGrowth,
                length: length,
                previousLength: length - 1));
        logActivity.Current = postSnapshot;
        logActivity.RaiseActivityChanged();

        var reacquired = Assert.Single(monitoring.Current.Contexts);
        Assert.NotEqual(retiredContextId, reacquired.ContextId);
        Assert.Equal(sourceA, reacquired.CurrentSourceId);
        Assert.Equal(clientC, reacquired.ProcessInstance);
        Assert.Empty(monitoring.Current.PendingOffers);
        Assert.Equal(2, gameplay.ResetCount);
        await ParserTestSnapshots.WaitUntilAsync(() => parser.Current.Workers.Count == 1);
        Assert.Equal(reacquired.ContextId, Assert.Single(parser.Current.Workers).ContextId);

        logActivity.RaiseActivityChanged();
        Assert.Single(monitoring.Current.Contexts);
        Assert.Equal(reacquired.ContextId, monitoring.Current.Contexts[0].ContextId);
        await ParserTestSnapshots.WaitUntilAsync(() => parser.Current.Workers.Count == 1);
    }

    [Fact]
    public async Task Log_activity_repairs_stale_cached_runtime_availability_after_missed_status_changed()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var source = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var now = time.GetUtcNow();
        var client = FakeGameRuntimeService.CreateClient(7_001, now);

        logActivity.Current = TestLogCandidates.Snapshot(
            now,
            TestLogCandidates.Create(source, LogSourceActivityState.Growing, now));

        using var monitoring = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = time });
        await monitoring.StartAsync();
        Assert.Single(monitoring.Current.Contexts);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);
        Assert.Equal(MonitoringContextState.RuntimeSuspended, monitoring.Current.Contexts[0].State);

        var resetAt = now.AddMinutes(1);
        time.Set(resetAt);
        monitoring.ResetForNewRuntimeGeneration();
        Assert.Empty(monitoring.Current.Contexts);

        // Authoritative runtime is Online again, but StatusChanged never reached monitoring.
        runtime.CurrentStatus = GameRuntimeStatus.Running;
        runtime.RunningClients = [client];
        runtime.RunningClientCount = 1;

        var postGrowth = resetAt.AddMinutes(1);
        time.Set(postGrowth);
        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            postGrowth,
            TestLogCandidates.Create(
                source,
                LogSourceActivityState.Growing,
                postGrowth,
                lastGrowthAt: postGrowth));
        logActivity.RaiseActivityChanged();

        var context = Assert.Single(monitoring.Current.Contexts);
        Assert.Equal(source, context.CurrentSourceId);
        Assert.Empty(monitoring.Current.PendingOffers);
        Assert.True(monitoring.GetDiagnostics().IsRuntimeAvailable);
    }

    [Fact]
    public async Task Earlier_status_subscriber_fault_does_not_block_monitoring_runtime_receipt()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var gameplay = new RecordingGameplaySessionManager();
        var source = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var now = time.GetUtcNow();
        var client = FakeGameRuntimeService.CreateClient(8_001, now);

        using var monitoring = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = time });
        using var generation = new LiveRuntimeGenerationService(
            runtime,
            monitoring,
            gameplay,
            new RecordingViewedContextService());

        runtime.StatusChanged += (_, _) =>
            throw new InvalidOperationException("earlier subscriber fault");

        await monitoring.StartAsync();

        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, [client]);
        logActivity.Current = TestLogCandidates.Snapshot(
            now,
            TestLogCandidates.Create(source, LogSourceActivityState.Growing, now));
        logActivity.RaiseActivityChanged();

        Assert.Single(monitoring.Current.Contexts);
        Assert.True(monitoring.GetDiagnostics().IsRuntimeAvailable);
    }

    private sealed class RecordingGameplaySessionManager : IGameplaySessionManager
    {
        public int ResetCount { get; private set; }

        public GameplaySessionManagerSnapshot Current { get; } = GameplaySessionManagerSnapshot.Empty;

        public event EventHandler<GameplaySessionManagerChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<GameplaySessionEventsAvailableEventArgs>? CommittedEventsAvailable
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public GameplaySessionOperationResult ConfirmCharacter(
            MonitoringContextId contextId,
            CharacterRecordId characterRecordId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ClearIdentity(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult StartTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult PauseTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ResumeTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult StopTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ResetTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionDiagnostics GetDiagnostics() =>
            GameplaySessionTestInfrastructure.IdleGameplayDiagnostics(isRunning: true);

        public void ResetForNewRuntimeGeneration() => ResetCount++;
    }

    private sealed class RecordingViewedContextService : IViewedContextService
    {
        public ViewedContextState Current { get; private set; } =
            TestGameplaySessionContextSupport.FollowingLive();

        public event EventHandler<ViewedContextChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public void SelectViewedAccount(string accountStableId) =>
            throw new NotSupportedException();

        public void SelectViewedCharacter(string accountStableId, CharacterRecordId characterRecordId) =>
            throw new NotSupportedException();

        public void ReturnToLive() => throw new NotSupportedException();

        public void SelectGameplaySessionContext(MonitoringContextId contextId) =>
            throw new NotSupportedException();

        public void ResetForNewRuntimeGeneration()
        {
            Current = TestGameplaySessionContextSupport.FollowingLive();
        }
    }
}

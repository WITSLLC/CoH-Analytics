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

    private sealed class RecordingGameplaySessionManager : IGameplaySessionManager
    {
        public int ResetCount { get; private set; }

        public GameplaySessionManagerSnapshot Current { get; } = GameplaySessionManagerSnapshot.Empty;

        public event EventHandler<GameplaySessionManagerChangedEventArgs>? StateChanged;

        public event EventHandler<GameplaySessionEventsAvailableEventArgs>? CommittedEventsAvailable;

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

        public event EventHandler<ViewedContextChangedEventArgs>? Changed;

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

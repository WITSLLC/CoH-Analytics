using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class LiveRuntimeProcessInstanceLifecycleTests
{
    private static readonly DateTimeOffset StartA = new(2026, 8, 14, 1, 15, 39, TimeSpan.Zero);
    private static readonly DateTimeOffset StartB = new(2026, 8, 14, 2, 15, 39, TimeSpan.Zero);
    private static readonly DateTimeOffset StartC = new(2026, 8, 14, 3, 15, 39, TimeSpan.Zero);
    private static readonly string ExecutablePath =
        @"C:\Games\Homecoming\bin\win64\live\cityofheroes.exe";

    [Fact]
    public void Single_client_exit_preserves_offline_runtime_state_without_reset()
    {
        var service = CreateService(out _, out var gameplay, out _, out var runtime);
        var client = CreateClient(3_524, StartA);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, [client]);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);

        Assert.Equal(0, gameplay.ResetCount);
    }

    [Fact]
    public void Relaunch_after_full_exit_resets_for_new_process_instance()
    {
        var service = CreateService(out var monitoring, out var gameplay, out var viewed, out var runtime);
        var originalClient = CreateClient(3_524, StartA);
        var relaunchedClient = CreateClient(3_524, StartB);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, [originalClient]);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, [relaunchedClient]);

        Assert.Equal(1, monitoring.ResetCount);
        Assert.Equal(1, gameplay.ResetCount);
        Assert.Equal(1, viewed.ResetCount);
    }

    [Fact]
    public void Same_pid_with_different_start_time_resets_without_observing_zero_clients()
    {
        var service = CreateService(out _, out var gameplay, out _, out var runtime);
        var originalClient = CreateClient(3_524, StartA);
        var reusedPidClient = CreateClient(3_524, StartB);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, [originalClient]);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, [reusedPidClient]);

        Assert.Equal(1, gameplay.ResetCount);
    }

    [Fact]
    public void Different_pid_after_full_exit_resets_for_new_process_instance()
    {
        var service = CreateService(out _, out var gameplay, out _, out var runtime);
        var originalClient = CreateClient(3_524, StartA);
        var replacementClient = CreateClient(6_356, StartC);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, [originalClient]);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, [replacementClient]);

        Assert.Equal(1, gameplay.ResetCount);
    }

    [Fact]
    public void Second_simultaneous_process_instance_does_not_reset_existing_client()
    {
        var service = CreateService(out _, out var gameplay, out _, out var runtime);
        var firstClient = CreateClient(3_524, StartA);
        var secondClient = CreateClient(6_356, StartB);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, [firstClient]);
        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [firstClient, secondClient]);

        Assert.Equal(0, gameplay.ResetCount);
    }

    [Fact]
    public void One_of_two_process_instances_exiting_does_not_reset_remaining_client()
    {
        var service = CreateService(out _, out var gameplay, out _, out var runtime);
        var firstClient = CreateClient(3_524, StartA);
        var secondClient = CreateClient(6_356, StartB);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, [firstClient]);
        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [firstClient, secondClient]);
        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [firstClient]);

        Assert.Equal(0, gameplay.ResetCount);
    }

    [Fact]
    public async Task New_process_instance_after_exit_does_not_inherit_old_character_or_telemetry()
    {
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var runtime = new FakeGameRuntimeService
            {
                CurrentStatus = GameRuntimeStatus.Running,
                RunningClientCount = 1
            };
            var logActivity = new FakeLogActivityService();
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
            var originalClient = CreateClient(3_524, StartA);
            var relaunchClient = CreateClient(3_524, StartB);
            var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
            var sessionStart = time.GetUtcNow();
            var sessionEnd = sessionStart.AddMinutes(5);
            var relaunchAt = sessionEnd.AddMinutes(1);

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
                new LiveRuntimeGenerationServiceTests.RecordingViewedContextService());
            var identity = new GameplaySessionIdentityReadService(gameplay, monitoring, repository);

            logActivity.Current = TestLogCandidates.Snapshot(
                sessionStart,
                TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, sessionStart));
            runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, [originalClient]);
            await monitoring.StartAsync();
            await gameplay.StartAsync();

            var contextId = Assert.Single(monitoring.Current.Contexts).ContextId;
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    contextId,
                    primarySource),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:01 You gain 900 experience.",
                    contextId,
                    primarySource,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => gameplay.Current.Sessions.Any(session => session.SessionExperienceGained == 900));

            runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);
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
                    lastGrowthAt: relaunchAt));
            runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, [relaunchClient]);
            logActivity.RaiseActivityChanged();

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => !gameplay.Current.Sessions.Any(session => session.SessionExperienceGained == 900));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:06:00 Welcome to City of Heroes, Alpha Hero!",
                    Assert.Single(monitoring.Current.Contexts).ContextId,
                    primarySource)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => gameplay.Current.Sessions.Any(session =>
                    string.Equals(session.CharacterDisplayName, "Alpha Hero", StringComparison.Ordinal)
                    && session.SessionExperienceGained == 0));

            Assert.DoesNotContain(
                identity.Current.Contexts,
                context => string.Equals(context.CharacterDisplayName, "Dawn's Vanguard", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Stale_old_account_log_cannot_resurrect_on_new_process_instance()
    {
        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClientCount = 1
        };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var gameplay = new LiveRuntimeGenerationServiceTests.RecordingGameplaySessionManager();

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");
        var sessionStart = time.GetUtcNow();
        var sessionEnd = sessionStart.AddMinutes(3);
        var relaunchAt = sessionEnd.AddMinutes(1);
        var originalClient = CreateClient(3_524, StartA);
        var relaunchClient = CreateClient(6_356, StartC);

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
            new LiveRuntimeGenerationServiceTests.RecordingViewedContextService());

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, [originalClient]);
        await monitoring.StartAsync();
        var primaryContext = Assert.Single(monitoring.Current.Contexts);
        Assert.Equal("TestAccount", primaryContext.AccountStableId);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, []);
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
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, [relaunchClient]);
        logActivity.RaiseActivityChanged();

        Assert.Equal(1, gameplay.ResetCount);
        var altContext = Assert.Single(monitoring.Current.Contexts);
        Assert.Equal("AltAccount", altContext.AccountStableId);
        Assert.DoesNotContain(
            monitoring.Current.Contexts,
            context => string.Equals(context.AccountStableId, "TestAccount", StringComparison.Ordinal));
        Assert.NotEqual(primaryContext.ContextId, altContext.ContextId);
    }

    private static HomecomingProcessInstance CreateClient(int processId, DateTimeOffset processStartTime) =>
        FakeGameRuntimeService.CreateClient(processId, processStartTime, ExecutablePath);

    private static LiveRuntimeGenerationService CreateService(
        out LiveRuntimeGenerationServiceTests.RecordingMonitoringSessionManager monitoring,
        out LiveRuntimeGenerationServiceTests.RecordingGameplaySessionManager gameplay,
        out LiveRuntimeGenerationServiceTests.RecordingViewedContextService viewed,
        out FakeGameRuntimeService runtime)
    {
        runtime = new FakeGameRuntimeService();
        monitoring = new LiveRuntimeGenerationServiceTests.RecordingMonitoringSessionManager();
        gameplay = new LiveRuntimeGenerationServiceTests.RecordingGameplaySessionManager();
        viewed = new LiveRuntimeGenerationServiceTests.RecordingViewedContextService();
        return new LiveRuntimeGenerationService(runtime, monitoring, gameplay, viewed);
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

using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class LiveRuntimeGenerationServiceTests
{
    [Fact]
    public void Startup_with_running_client_does_not_reset()
    {
        var service = CreateService(out var monitoring, out var gameplay, out var viewed, out var runtime);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, runningClientCount: 1);

        Assert.Equal(0, monitoring.ResetCount);
        Assert.Equal(0, gameplay.ResetCount);
        Assert.Equal(0, viewed.ResetCount);
    }

    [Fact]
    public void Single_client_exit_does_not_reset()
    {
        var service = CreateService(out _, out var gameplay, out _, out var runtime);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, runningClientCount: 1);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, runningClientCount: 0);

        Assert.Equal(0, gameplay.ResetCount);
    }

    [Fact]
    public void Zero_to_one_after_prior_exit_resets_once()
    {
        var service = CreateService(out var monitoring, out var gameplay, out var viewed, out var runtime);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, runningClientCount: 1);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, runningClientCount: 0);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, runningClientCount: 1);

        Assert.Equal(1, monitoring.ResetCount);
        Assert.Equal(1, gameplay.ResetCount);
        Assert.Equal(1, viewed.ResetCount);
    }

    [Fact]
    public void Second_simultaneous_client_does_not_reset()
    {
        var service = CreateService(out _, out var gameplay, out _, out var runtime);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, runningClientCount: 1);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, runningClientCount: 2);

        Assert.Equal(0, gameplay.ResetCount);
    }

    [Fact]
    public void One_of_two_clients_exiting_does_not_reset()
    {
        var service = CreateService(out _, out var gameplay, out _, out var runtime);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, runningClientCount: 1);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, runningClientCount: 2);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, runningClientCount: 1);

        Assert.Equal(0, gameplay.ResetCount);
    }

    [Fact]
    public void Repeated_zero_polling_does_not_repeat_reset()
    {
        var service = CreateService(out _, out var gameplay, out _, out var runtime);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, runningClientCount: 1);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, runningClientCount: 0);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Off, runningClientCount: 0);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, runningClientCount: 1);

        Assert.Equal(1, gameplay.ResetCount);
    }

    [Fact]
    public void Same_pid_different_start_time_turnover_resets_once()
    {
        var service = CreateService(out _, out var gameplay, out _, out var runtime);
        var startA = new DateTimeOffset(2026, 8, 14, 1, 15, 39, TimeSpan.Zero);
        var startB = new DateTimeOffset(2026, 8, 14, 2, 15, 39, TimeSpan.Zero);
        var originalClient = FakeGameRuntimeService.CreateClient(3_524, startA);
        var reusedPidClient = FakeGameRuntimeService.CreateClient(3_524, startB);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, [originalClient]);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, [reusedPidClient]);

        Assert.Equal(1, gameplay.ResetCount);
    }

    [Fact]
    public void Same_pid_earlier_refined_start_time_does_not_reset()
    {
        var service = CreateService(out _, out var gameplay, out _, out var runtime);
        var provisionalStart = new DateTimeOffset(2026, 8, 14, 1, 15, 39, TimeSpan.Zero);
        var refinedStart = provisionalStart.AddMinutes(-2);
        var provisionalClient = FakeGameRuntimeService.CreateClient(3_524, provisionalStart);
        var refinedClient = FakeGameRuntimeService.CreateClient(3_524, refinedStart);

        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Unconfigured,
            GameRuntimeStatus.Running,
            [provisionalClient]);
        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [refinedClient]);

        Assert.Equal(0, gameplay.ResetCount);
    }

    [Theory]
    [InlineData(0, 1, false, false, false)]
    [InlineData(1, 1, true, false, false)]
    [InlineData(1, 0, true, true, false)]
    [InlineData(0, 1, true, true, true)]
    [InlineData(0, 2, true, true, true)]
    [InlineData(2, 1, true, true, false)]
    public void TryConsumeNewGenerationBoundary_matches_expected_transitions(
        int previousCount,
        int nextCount,
        bool observedNonZero,
        bool observedZeroAfterNonZero,
        bool expectedReset)
    {
        var nonZero = observedNonZero;
        var zeroAfterNonZero = observedZeroAfterNonZero;
        var args = new GameRuntimeStatusChangedEventArgs(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            nextCount,
            previousCount);

        var reset = LiveRuntimeGenerationService.TryConsumeNewGenerationBoundary(
            args,
            ref nonZero,
            ref zeroAfterNonZero);

        Assert.Equal(expectedReset, reset);
    }

    internal static LiveRuntimeGenerationService CreateService(
        out RecordingMonitoringSessionManager monitoring,
        out RecordingGameplaySessionManager gameplay,
        out RecordingViewedContextService viewed,
        out FakeGameRuntimeService runtime)
    {
        runtime = new FakeGameRuntimeService();
        monitoring = new RecordingMonitoringSessionManager();
        gameplay = new RecordingGameplaySessionManager();
        viewed = new RecordingViewedContextService();
        return new LiveRuntimeGenerationService(runtime, monitoring, gameplay, viewed);
    }

    internal sealed class RecordingMonitoringSessionManager : IMonitoringSessionManager
    {
        public int ResetCount { get; private set; }

        public MonitoringSessionManagerSnapshot Current { get; private set; } =
            MonitoringSessionManagerSnapshot.Empty;

        public bool IsRuntimeAvailable => true;

        public event EventHandler<MonitoringSessionManagerChangedEventArgs>? StateChanged;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public MonitoringSourceClaimResult ClaimSource(MonitoringContextId contextId, LogSourceId sourceId) =>
            throw new NotSupportedException();

        public MonitoringSourceClaimResult ReleaseSource(MonitoringContextId contextId) =>
            throw new NotSupportedException();

        public MonitoringSourceClaimResult RemoveContext(MonitoringContextId contextId) =>
            throw new NotSupportedException();

        public MonitoringSourceDecision AcceptOffer(MonitoringSourceOfferId offerId) =>
            throw new NotSupportedException();

        public MonitoringSourceDecision DeclineOffer(MonitoringSourceOfferId offerId) =>
            throw new NotSupportedException();

        public MonitoringSourceDecision ReconsiderSource(LogSourceId sourceId) =>
            throw new NotSupportedException();

        public void ResetForNewRuntimeGeneration(bool observedZeroClientCountAfterNonZero = true) => ResetCount++;
    }

    internal sealed class RecordingGameplaySessionManager : IGameplaySessionManager
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

    internal sealed class RecordingViewedContextService : IViewedContextService
    {
        public int ResetCount { get; private set; }

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
            ResetCount++;
            Current = TestGameplaySessionContextSupport.FollowingLive();
        }
    }
}

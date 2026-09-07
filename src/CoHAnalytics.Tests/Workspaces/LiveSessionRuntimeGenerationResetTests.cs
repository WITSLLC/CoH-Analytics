using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class LiveSessionRuntimeGenerationResetTests
{
    [Fact]
    public void New_different_account_generation_clears_prior_offline_session()
    {
        var contextA = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateResolvedContext(
                    contextA,
                    recordA,
                    "Hero A",
                    startedAt,
                    accountStableId: "acct-a",
                    experience: 2_400))
        };

        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClientCount = 1
        };

        using var viewModel = CreateViewModel(identity, runtime);
        DrainDispatcher();

        Assert.Equal("Hero A", viewModel.ViewedCharacterLabel);
        Assert.Equal(Format(2_400), viewModel.SessionExperienceLabel);

        identity.Current = Snapshot(
            CreateOfflineEndedContext(contextA, recordA, "Hero A", startedAt, experience: 2_400));
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, runningClientCount: 0);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Frozen, viewModel.WorkspaceState);
        Assert.Equal("Hero A", viewModel.ViewedCharacterLabel);

        var contextB = MonitoringContextId.CreateNew();
        identity.Current = Snapshot(
            CreateResolvedContext(
                contextB,
                CharacterRecordId.CreateNew(),
                "Hero B",
                startedAt.AddMinutes(30),
                accountStableId: "acct-b",
                experience: 10,
                influence: 2));
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, runningClientCount: 1);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
        Assert.Equal("Hero B", viewModel.ViewedCharacterLabel);
        Assert.Equal(Format(10), viewModel.SessionExperienceLabel);
        Assert.Equal(Format(2), viewModel.SessionGameplayInfluenceLabel);
        Assert.False(string.Equals("Hero A", viewModel.ViewedCharacterLabel, StringComparison.Ordinal));
    }

    [Fact]
    public void New_same_account_generation_does_not_resume_old_totals()
    {
        var contextId = MonitoringContextId.CreateNew();
        var recordId = CharacterRecordId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateResolvedContext(contextId, recordId, "Same Hero", startedAt, experience: 5_000))
        };

        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClientCount = 1
        };

        using var viewModel = CreateViewModel(identity, runtime);
        DrainDispatcher();

        identity.Current = Snapshot(
            CreateOfflineEndedContext(contextId, recordId, "Same Hero", startedAt, experience: 5_000));
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, runningClientCount: 0);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(Format(5_000), viewModel.SessionExperienceLabel);

        var newContextId = MonitoringContextId.CreateNew();
        identity.Current = Snapshot(
            CreateResolvedContext(
                newContextId,
                CharacterRecordId.CreateNew(),
                "Same Hero",
                startedAt.AddHours(2),
                experience: 0,
                influence: 0));
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, runningClientCount: 1);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
        Assert.Equal("Same Hero", viewModel.ViewedCharacterLabel);
        Assert.Equal(Format(0), viewModel.SessionExperienceLabel);
        Assert.Equal(Format(0), viewModel.SessionGameplayInfluenceLabel);
    }

    [Fact]
    public void Second_simultaneous_client_does_not_clear_presentation()
    {
        var contextId = MonitoringContextId.CreateNew();
        var recordId = CharacterRecordId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateResolvedContext(contextId, recordId, "Hero A", startedAt, experience: 1_000))
        };

        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClientCount = 1
        };

        using var viewModel = CreateViewModel(identity, runtime);
        DrainDispatcher();

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Running, runningClientCount: 2);
        DrainDispatcher();

        Assert.Equal("Hero A", viewModel.ViewedCharacterLabel);
        Assert.Equal(Format(1_000), viewModel.SessionExperienceLabel);
        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
    }

    [Fact]
    public void New_generation_clears_unresolved_manual_selection_state()
    {
        var contextA = MonitoringContextId.CreateNew();
        var candidate = CharacterRecordId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(CreateUnresolvedContext(contextA, candidate))
        };

        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClientCount = 1
        };

        using var viewModel = CreateViewModel(identity, runtime);
        DrainDispatcher();
        Assert.True(viewModel.ViewedRequiresManualSelection);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, runningClientCount: 0);
        identity.Current = Snapshot(
            CreateOfflineEndedContext(contextA, candidate, "Pending Hero", startedAt, experience: 0) with
            {
                RequiresManualSelection = false
            });
        identity.RaiseChanged();
        DrainDispatcher();

        var contextB = MonitoringContextId.CreateNew();
        identity.Current = Snapshot(
            CreateResolvedContext(
                contextB,
                CharacterRecordId.CreateNew(),
                "Resolved Hero",
                startedAt.AddMinutes(10),
                accountStableId: "acct-b"));
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, runningClientCount: 1);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal("Resolved Hero", viewModel.ViewedCharacterLabel);
        Assert.False(viewModel.ViewedRequiresManualSelection);
    }

    [Fact]
    public void Repeated_zero_runtime_polling_preserves_offline_session_until_launch()
    {
        var contextId = MonitoringContextId.CreateNew();
        var recordId = CharacterRecordId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateResolvedContext(contextId, recordId, "Hero A", startedAt, experience: 900))
        };

        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClientCount = 1
        };

        using var viewModel = CreateViewModel(identity, runtime);
        DrainDispatcher();

        identity.Current = Snapshot(
            CreateOfflineEndedContext(contextId, recordId, "Hero A", startedAt, experience: 900));
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, runningClientCount: 0);
        identity.RaiseChanged();
        DrainDispatcher();

        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Off, runningClientCount: 0);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Off, runningClientCount: 0);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Frozen, viewModel.WorkspaceState);
        Assert.Equal("Hero A", viewModel.ViewedCharacterLabel);
        Assert.Equal(Format(900), viewModel.SessionExperienceLabel);
    }

    private static LiveSessionViewModel CreateViewModel(
        FakeIdentityReadService identity,
        FakeGameRuntimeService runtime)
    {
        var viewedContextService = new TestGameplaySessionContextSupport.FakeViewedContextService(
            TestGameplaySessionContextSupport.FollowingLive());
        var resolver = new GameplaySessionContextResolver(identity, viewedContextService);
        return new LiveSessionViewModel(
            new FakeApplicationOrchestrator(),
            runtime,
            new TestGameplaySessionContextSupport.FakeGameplaySessionManager(),
            identity,
            resolver,
            viewedContextService);
    }

    private static LiveMonitoringContextIdentityReadModel CreateResolvedContext(
        MonitoringContextId contextId,
        CharacterRecordId recordId,
        string displayName,
        DateTimeOffset startedAt,
        string accountStableId = "acct-1",
        long experience = 0,
        long influence = 0) =>
        new()
        {
            ContextId = contextId,
            ContextState = MonitoringContextState.Ready,
            AccountStableId = accountStableId,
            AccountDisplayName = accountStableId,
            CharacterRecordId = recordId,
            CharacterDisplayName = displayName,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Resolved,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            HasActiveSession = true,
            IdentityStatusLabel = "Confirmed",
            IdentityDetail = $"Active character: {displayName}",
            SessionStartedAt = startedAt,
            SessionExperienceGained = experience,
            SessionGameplayInfluenceGained = influence
        };

    private static LiveMonitoringContextIdentityReadModel CreateOfflineEndedContext(
        MonitoringContextId contextId,
        CharacterRecordId recordId,
        string displayName,
        DateTimeOffset startedAt,
        long experience) =>
        CreateResolvedContext(contextId, recordId, displayName, startedAt, experience: experience) with
        {
            ContextState = MonitoringContextState.RuntimeSuspended,
            SessionLifecycleState = GameplaySessionLifecycleState.Suspended,
            HasActiveSession = false,
            SessionTimingEndAt = startedAt.AddHours(1),
            IdentityStatusLabel = "Ended",
            IdentityDetail = $"Last character: {displayName}"
        };

    private static LiveMonitoringContextIdentityReadModel CreateUnresolvedContext(
        MonitoringContextId contextId,
        CharacterRecordId characterId) =>
        new()
        {
            ContextId = contextId,
            ContextState = MonitoringContextState.Ready,
            AccountStableId = "acct-1",
            AccountDisplayName = "acct-1",
            CharacterIdentityConfidence = CharacterIdentityConfidence.Unknown,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.IdentityRequired,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            HasActiveSession = true,
            RequiresManualSelection = true,
            IdentityStatusLabel = "Identity Required",
            IdentityDetail = "Select the active character.",
            SessionStartedAt = DateTimeOffset.UtcNow,
            PickerCharacters =
            [
                new CharacterPickerOptionReadModel
                {
                    RecordId = characterId,
                    DisplayName = "Example Hero",
                    LastObservedAt = DateTimeOffset.UtcNow
                }
            ]
        };

    private static GameplaySessionIdentityReadModelSnapshot Snapshot(
        LiveMonitoringContextIdentityReadModel context,
        long revision = 1) =>
        GameplaySessionIdentityReadModelSnapshot.Create([context], DateTimeOffset.UtcNow, revision);

    private static string Format(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static void DrainDispatcher()
    {
        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }
    }

    private sealed class FakeIdentityReadService : IGameplaySessionIdentityReadService
    {
        public GameplaySessionIdentityReadModelSnapshot Current { get; set; } =
            GameplaySessionIdentityReadModelSnapshot.Empty;

        public event EventHandler<GameplaySessionIdentityReadModelChangedEventArgs>? Changed;

        public void RaiseChanged() =>
            Changed?.Invoke(
                this,
                new GameplaySessionIdentityReadModelChangedEventArgs { Snapshot = Current });
    }

    private sealed class FakeApplicationOrchestrator : IApplicationOrchestrator
    {
        public ApplicationStateSnapshot Current { get; set; } =
            ApplicationStateSnapshot.Empty(DateTimeOffset.UtcNow);

        public event EventHandler<ApplicationStateChangedEventArgs>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task RefreshAsync(string? providerId = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

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
public sealed class LiveSessionOfflineLifecycleTests
{
    [Fact]
    public void App_starts_offline_without_character_picker()
    {
        using var viewModel = CreateViewModel(
            GameplaySessionIdentityReadModelSnapshot.Empty,
            GameRuntimeStatus.Off);
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Waiting, viewModel.WorkspaceState);
        Assert.False(viewModel.ViewedRequiresManualSelection);
        Assert.Equal("Unknown", viewModel.ViewedCharacterLabel);
    }

    [Fact]
    public void Running_unresolved_session_can_show_character_picker()
    {
        var contextId = MonitoringContextId.CreateNew();
        var characterId = CharacterRecordId.CreateNew();
        var snapshot = Snapshot(CreateUnresolvedContext(contextId, characterId));

        using var viewModel = CreateViewModel(snapshot, GameRuntimeStatus.Running);
        DrainDispatcher();

        Assert.True(viewModel.ViewedRequiresManualSelection);
        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
    }

    [Fact]
    public void Running_resolved_session_shows_normal_live_state()
    {
        var contextId = MonitoringContextId.CreateNew();
        var recordId = CharacterRecordId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var snapshot = Snapshot(
            CreateResolvedContext(contextId, recordId, "Example Hero", startedAt, experience: 1_500));

        using var viewModel = CreateViewModel(snapshot, GameRuntimeStatus.Running);
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
        Assert.Equal("Example Hero", viewModel.ViewedCharacterLabel);
        Assert.False(viewModel.ViewedRequiresManualSelection);
        Assert.Equal(Format(1_500), viewModel.SessionExperienceLabel);
    }

    [Fact]
    public void Homecoming_exit_preserves_session_data_identity_and_hides_picker()
    {
        var contextId = MonitoringContextId.CreateNew();
        var recordId = CharacterRecordId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateResolvedContext(contextId, recordId, "Example Hero", startedAt, experience: 2_400))
        };

        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClientCount = 1
        };
        using var viewModel = CreateViewModel(identity, runtime);
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
        Assert.Equal("Example Hero", viewModel.ViewedCharacterLabel);

        identity.Current = Snapshot(
            CreateOfflineEndedContext(contextId, recordId, "Example Hero", startedAt, experience: 2_400));
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, runningClientCount: 0);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Frozen, viewModel.WorkspaceState);
        Assert.Equal("Example Hero", viewModel.ViewedCharacterLabel);
        Assert.Equal(Format(2_400), viewModel.SessionExperienceLabel);
        Assert.False(viewModel.ViewedRequiresManualSelection);
        Assert.Equal("Ended", viewModel.ViewedStatusLabel);
    }

    [Fact]
    public void Homecoming_relaunch_starts_fresh_live_session_instead_of_resuming_previous()
    {
        var contextId = MonitoringContextId.CreateNew();
        var recordId = CharacterRecordId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var newStartedAt = startedAt.AddMinutes(45);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateResolvedContext(contextId, recordId, "Example Hero", startedAt, experience: 2_400))
        };

        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClientCount = 1
        };
        using var viewModel = CreateViewModel(identity, runtime);
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
        Assert.Equal("Example Hero", viewModel.ViewedCharacterLabel);
        Assert.Equal(Format(2_400), viewModel.SessionExperienceLabel);

        identity.Current = Snapshot(
            CreateOfflineEndedContext(contextId, recordId, "Example Hero", startedAt, experience: 2_400));
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off, runningClientCount: 0);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Frozen, viewModel.WorkspaceState);
        Assert.Equal(Format(2_400), viewModel.SessionExperienceLabel);

        identity.Current = Snapshot(
            CreateResolvedContext(
                contextId,
                CharacterRecordId.CreateNew(),
                "Fresh Hero",
                newStartedAt,
                experience: 25,
                influence: 3));
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, runningClientCount: 1);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
        Assert.Equal("Fresh Hero", viewModel.ViewedCharacterLabel);
        Assert.Equal(Format(25), viewModel.SessionExperienceLabel);
        Assert.Equal(Format(3), viewModel.SessionGameplayInfluenceLabel);
        Assert.False(viewModel.ViewedRequiresManualSelection);
    }

    [Fact]
    public void Repeated_offline_snapshot_refresh_does_not_mutate_preserved_session()
    {
        var contextId = MonitoringContextId.CreateNew();
        var recordId = CharacterRecordId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateOfflineEndedContext(contextId, recordId, "Example Hero", startedAt, experience: 900),
                revision: 1)
        };

        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off };
        using var viewModel = CreateViewModel(identity, runtime);
        DrainDispatcher();

        identity.Current = Snapshot(
            CreateOfflineEndedContext(contextId, recordId, "Example Hero", startedAt, experience: 900),
            revision: 2);
        identity.RaiseChanged();
        DrainDispatcher();

        identity.Current = Snapshot(
            CreateOfflineEndedContext(contextId, recordId, "Example Hero", startedAt, experience: 900),
            revision: 3);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Frozen, viewModel.WorkspaceState);
        Assert.Equal("Example Hero", viewModel.ViewedCharacterLabel);
        Assert.Equal(Format(900), viewModel.SessionExperienceLabel);
        Assert.False(viewModel.ViewedRequiresManualSelection);
    }

    private static LiveSessionViewModel CreateViewModel(
        GameplaySessionIdentityReadModelSnapshot snapshot,
        GameRuntimeStatus runtimeStatus)
    {
        var identity = new FakeIdentityReadService { Current = snapshot };
        var runtime = new FakeGameRuntimeService { CurrentStatus = runtimeStatus };
        return CreateViewModel(identity, runtime);
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
        long experience = 0,
        long influence = 0) =>
        new()
        {
            ContextId = contextId,
            ContextState = MonitoringContextState.Ready,
            AccountStableId = "acct-1",
            AccountDisplayName = "acct-1",
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
        CreateResolvedContext(contextId, recordId, displayName, startedAt, experience) with
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

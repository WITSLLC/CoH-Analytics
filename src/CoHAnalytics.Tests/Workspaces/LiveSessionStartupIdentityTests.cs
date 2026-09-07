using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class LiveSessionStartupIdentityTests
{
    [Fact]
    public void Monitoring_ready_without_session_hydrates_live_session_account()
    {
        var contextId = MonitoringContextId.CreateNew();
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(CreateMonitoringReadyContext(
                contextId,
                "testaccount",
                "TestAccount"))
        };

        using var viewModel = CreateLiveSession(identity);
        DrainDispatcher();

        Assert.True(viewModel.HasContexts);
        Assert.Equal("TestAccount", viewModel.ViewedAccountLabel);
        Assert.Equal("Unknown", viewModel.ViewedCharacterLabel);
        Assert.False(viewModel.ViewedRequiresManualSelection);
    }

    [Fact]
    public void ViewModel_created_after_manager_state_hydrates_immediately()
    {
        var contextId = MonitoringContextId.CreateNew();
        var recordId = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(
                    contextId,
                    "testaccount",
                    recordId,
                    "Dawn's Vanguard",
                    startedAt))
        };

        using var viewModel = CreateLiveSession(identity);
        DrainDispatcher();

        Assert.Equal("testaccount", viewModel.ViewedAccountLabel);
        Assert.Equal("Dawn's Vanguard", viewModel.ViewedCharacterLabel);
        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
    }

    [Fact]
    public void Pinned_accounts_character_does_not_change_live_session_identity()
    {
        var contextId = MonitoringContextId.CreateNew();
        var liveRecordId = CharacterRecordId.CreateNew();
        var browseRecordId = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(
                    contextId,
                    "testaccount",
                    liveRecordId,
                    "Dawn's Vanguard",
                    startedAt))
        };

        var repository = CreateRepository();
        var viewed = new ViewedContextService(identity, repository);
        viewed.SelectViewedCharacter("other-account", browseRecordId);

        using var live = CreateLiveSession(identity, viewed);
        DrainDispatcher();

        Assert.Equal("testaccount", live.ViewedAccountLabel);
        Assert.Equal("Dawn's Vanguard", live.ViewedCharacterLabel);
    }

    [Fact]
    public void Active_session_with_unresolved_character_shows_account_and_manual_selection()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(CreateUnresolvedActiveSessionContext(
                contextId,
                "testaccount",
                "TestAccount",
                startedAt,
                pickerCharacterId: CharacterRecordId.CreateNew(),
                pickerDisplayName: "Dawn's Vanguard"))
        };

        var repository = CreateRepository();
        var viewed = new ViewedContextService(identity, repository);
        var resolver = new GameplaySessionContextResolver(identity, viewed);
        var gameplay = new TestGameplaySessionContextSupport.FakeGameplaySessionManager();
        using var viewModel = CreateLiveSession(
            identity,
            viewed,
            resolver,
            gameplay,
            new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running });
        DrainDispatcher();

        Assert.Equal("TestAccount", viewModel.ViewedAccountLabel);
        Assert.Equal("Unknown", viewModel.ViewedCharacterLabel);
        Assert.True(viewModel.ViewedRequiresManualSelection);
    }

    [Fact]
    public void Identity_update_after_subscribe_hydrates_live_session_account()
    {
        var contextId = MonitoringContextId.CreateNew();
        var identity = new FakeIdentityReadService
        {
            Current = GameplaySessionIdentityReadModelSnapshot.Empty
        };

        using var viewModel = CreateLiveSession(identity);
        DrainDispatcher();
        Assert.False(viewModel.HasContexts);

        identity.Current = Snapshot(CreateMonitoringReadyContext(
            contextId,
            "testaccount",
            "TestAccount"));
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.True(viewModel.HasContexts);
        Assert.Equal("TestAccount", viewModel.ViewedAccountLabel);
    }

    private static LiveSessionViewModel CreateLiveSession(
        FakeIdentityReadService identity,
        ViewedContextService? viewed = null,
        IGameplaySessionContextResolver? resolver = null,
        IGameplaySessionManager? gameplay = null,
        IGameRuntimeService? gameRuntime = null)
    {
        var repository = CreateRepository();
        viewed ??= new ViewedContextService(identity, repository);
        resolver ??= new GameplaySessionContextResolver(identity, viewed);
        gameplay ??= new TestGameplaySessionContextSupport.FakeGameplaySessionManager();
        gameRuntime ??= new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off };

        return new LiveSessionViewModel(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            gameRuntime,
            gameplay,
            identity,
            resolver,
            viewed);
    }

    private static CharacterRepository CreateRepository()
    {
        var dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "coh-live-session-startup",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataDirectory);
        return new CharacterRepository(new CharacterRepositoryOptions { DataDirectory = dataDirectory });
    }

    private static LiveMonitoringContextIdentityReadModel CreateMonitoringReadyContext(
        MonitoringContextId contextId,
        string accountStableId,
        string accountDisplayName) =>
        new()
        {
            ContextId = contextId,
            ContextState = MonitoringContextState.Ready,
            AccountStableId = accountStableId,
            AccountDisplayName = accountDisplayName,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Unknown,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Unresolved,
            SessionLifecycleState = GameplaySessionLifecycleState.Finalized,
            HasActiveSession = false,
            NeedsAttention = false,
            CandidateCount = 0,
            IdentityStatusLabel = "Unknown",
            IdentityDetail = "Character identity has not been established yet.",
            RequiresManualSelection = false,
            PickerCharacters = []
        };

    private static LiveMonitoringContextIdentityReadModel CreateUnresolvedActiveSessionContext(
        MonitoringContextId contextId,
        string accountStableId,
        string accountDisplayName,
        DateTimeOffset startedAt,
        CharacterRecordId pickerCharacterId,
        string pickerDisplayName) =>
        new()
        {
            ContextId = contextId,
            ContextState = MonitoringContextState.Ready,
            AccountStableId = accountStableId,
            AccountDisplayName = accountDisplayName,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Unknown,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Candidate,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            HasActiveSession = true,
            NeedsAttention = false,
            CandidateCount = 1,
            IdentityStatusLabel = "Unknown",
            IdentityDetail = "Select a character or wait for a Welcome message.",
            RequiresManualSelection = true,
            SessionStartedAt = startedAt,
            PickerCharacters =
            [
                new CharacterPickerOptionReadModel
                {
                    RecordId = pickerCharacterId,
                    DisplayName = pickerDisplayName,
                    LastObservedAt = startedAt
                }
            ]
        };

    private static GameplaySessionIdentityReadModelSnapshot Snapshot(
        params LiveMonitoringContextIdentityReadModel[] contexts) =>
        GameplaySessionIdentityReadModelSnapshot.Create(
            contexts,
            DateTimeOffset.UtcNow,
            revision: 1);

    private static void DrainDispatcher()
    {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        dispatcher.Invoke(DispatcherPriority.Background, static () => { });
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
}

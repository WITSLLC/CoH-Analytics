using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class LiveSessionMultiContextSelectionTests
{
    [Fact]
    public void Single_active_session_hides_session_tabs()
    {
        var contextId = MonitoringContextId.CreateNew();
        var identity = CreateIdentity(contextId, "Hero A", experience: 1_000);
        using var viewModel = CreateLiveSession(identity, Following(contextId));
        DrainDispatcher();

        Assert.False(viewModel.ShowSessionTabs);
        Assert.Empty(viewModel.SessionTabs);
    }

    [Fact]
    public void Two_active_sessions_show_session_tabs()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var identity = CreateIdentity(
            (contextA, "Hero A", 1_000),
            (contextB, "Hero B", 2_000));
        using var viewModel = CreateLiveSession(identity, Following(contextA));
        DrainDispatcher();

        Assert.True(viewModel.ShowSessionTabs);
        Assert.Equal(2, viewModel.SessionTabs.Count);
        Assert.Contains(viewModel.SessionTabs, tab => tab.PrimaryLabel == "Hero A");
        Assert.Contains(viewModel.SessionTabs, tab => tab.PrimaryLabel == "Hero B");
    }

    [Fact]
    public void First_active_session_remains_selected_when_second_starts()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeGameplaySessionIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(
                    contextA, "acct-1", recordA, "Hero A", startedAt, experience: 1_000))
        };

        var repository = CreateRepository();
        var viewed = new ViewedContextService(identity, repository);
        var resolver = new GameplaySessionContextResolver(identity, viewed);
        using var live = CreateLiveSession(identity, viewed, resolver);
        DrainDispatcher();

        Assert.Equal(contextA, viewed.Current.LiveFollowContextId);
        Assert.Equal(Format(1_000), live.SessionExperienceLabel);

        identity.Current = Snapshot(
            TestGameplaySessionContextSupport.CreateContext(
                contextA, "acct-1", recordA, "Hero A", startedAt, experience: 1_000),
            TestGameplaySessionContextSupport.CreateContext(
                contextB, "acct-2", recordB, "Hero B", startedAt, experience: 99_000));
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(contextA, viewed.Current.LiveFollowContextId);
        Assert.Equal(Format(1_000), live.SessionExperienceLabel);
        Assert.True(live.ShowSessionTabs);
    }

    [Fact]
    public void Explicit_tab_selection_updates_live_session_and_analytics()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var identity = CreateIdentity(
            (contextA, "Hero A", 1_000),
            (contextB, "Hero B", 8_000));
        var viewed = new TestGameplaySessionContextSupport.FakeViewedContextService(
            TestGameplaySessionContextSupport.FollowingLive(contextA));
        var resolver = new GameplaySessionContextResolver(identity, viewed);
        using var live = CreateLiveSession(identity, viewed, resolver);
        using var analytics = CreateAnalytics(identity, viewed, resolver);
        DrainDispatcher();

        var tabB = live.SessionTabs.Single(tab => tab.ContextId == contextB);
        live.SelectSessionTabCommand.Execute(tabB);
        DrainDispatcher();

        Assert.Equal(contextB, viewed.Current.LiveFollowContextId);
        Assert.Equal(Format(8_000), live.SessionExperienceLabel);
        Assert.Equal("Hero B", analytics.ContextCharacterLabel);
    }

    [Fact]
    public void Account_anonymity_masks_both_session_surfaces_without_changing_selection_or_resolver_identity()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var identity = CreateIdentity(
            (contextA, "Hero A", 1_000),
            (contextB, "Hero B", 8_000));
        var viewed = new TestGameplaySessionContextSupport.FakeViewedContextService(
            TestGameplaySessionContextSupport.FollowingLive(contextA));
        var resolver = new GameplaySessionContextResolver(identity, viewed);
        var anonymity = new AccountAnonymityService(StubInternalFeatureGate.Enabled);
        using var live = new LiveSessionViewModel(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off },
            new TestGameplaySessionContextSupport.FakeGameplaySessionManager(),
            identity,
            resolver,
            viewed,
            accountAnonymityService: anonymity);
        using var analytics = new AnalyticsViewModel(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off },
            identity,
            resolver,
            viewed,
            anonymity);
        DrainDispatcher();

        Assert.Equal("acct-1", analytics.ContextAccountLabel);
        Assert.Equal("acct-1", live.Contexts.Single(panel => panel.ContextId == contextA).AccountLabel);

        anonymity.SetEnabled(true);
        DrainDispatcher();

        Assert.Equal("██████", analytics.ContextAccountLabel);
        Assert.All(live.Contexts, panel => Assert.Equal("██████", panel.AccountLabel));
        Assert.All(live.SessionTabs, tab => Assert.Equal("██████", tab.SecondaryLabel));
        Assert.Equal("Hero A", analytics.ContextCharacterLabel);

        live.SelectSessionTabCommand.Execute(live.SessionTabs.Single(tab => tab.ContextId == contextB));
        DrainDispatcher();

        Assert.Equal(contextB, viewed.Current.LiveFollowContextId);
        Assert.Equal("acct-2", resolver.Resolve().Context!.AccountStableId);
        Assert.Equal("acct-2", resolver.Resolve().Context!.AccountDisplayName);
        Assert.Equal("Hero B", analytics.ContextCharacterLabel);
        Assert.Equal("██████", analytics.ContextAccountLabel);

        anonymity.SetEnabled(false);
        DrainDispatcher();

        Assert.Equal("acct-2", analytics.ContextAccountLabel);
        Assert.All(live.Contexts, panel => Assert.StartsWith("acct-", panel.AccountLabel));
        Assert.All(live.SessionTabs, tab => Assert.StartsWith("acct-", tab.SecondaryLabel));
    }

    [Fact]
    public void Selection_survives_workspace_refresh_via_viewed_context()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var identity = CreateIdentity(
            (contextA, "Hero A", 1_000),
            (contextB, "Hero B", 8_000));
        var viewed = new TestGameplaySessionContextSupport.FakeViewedContextService(
            TestGameplaySessionContextSupport.FollowingLive(contextA));
        var resolver = new GameplaySessionContextResolver(identity, viewed);

        using (var live = CreateLiveSession(identity, viewed, resolver))
        {
            DrainDispatcher();
            live.SelectSessionTabCommand.Execute(live.SessionTabs.Single(tab => tab.ContextId == contextB));
            DrainDispatcher();
        }

        using var liveAgain = CreateLiveSession(identity, viewed, resolver);
        DrainDispatcher();

        Assert.Equal(contextB, viewed.Current.LiveFollowContextId);
        Assert.Equal(Format(8_000), liveAgain.SessionExperienceLabel);
        Assert.True(liveAgain.SessionTabs.Single(tab => tab.ContextId == contextB).IsSelected);
    }

    [Fact]
    public void Background_session_accumulates_while_another_is_displayed()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeGameplaySessionIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(
                    contextA, "acct-1", recordA, "Hero A", startedAt, experience: 1_000),
                TestGameplaySessionContextSupport.CreateContext(
                    contextB, "acct-2", recordB, "Hero B", startedAt, experience: 2_000))
        };
        var viewed = new TestGameplaySessionContextSupport.FakeViewedContextService(
            TestGameplaySessionContextSupport.FollowingLive(contextA));
        var resolver = new GameplaySessionContextResolver(identity, viewed);
        using var live = CreateLiveSession(identity, viewed, resolver);
        DrainDispatcher();

        identity.Current = Snapshot(
            TestGameplaySessionContextSupport.CreateContext(
                contextA, "acct-1", recordA, "Hero A", startedAt, experience: 1_100),
            TestGameplaySessionContextSupport.CreateContext(
                contextB, "acct-2", recordB, "Hero B", startedAt, experience: 7_500));
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(Format(1_100), live.SessionExperienceLabel);

        live.SelectSessionTabCommand.Execute(live.SessionTabs.Single(tab => tab.ContextId == contextB));
        DrainDispatcher();

        Assert.Equal(Format(7_500), live.SessionExperienceLabel);
    }

    [Fact]
    public void Clear_session_presentation_baselines_are_isolated_per_context()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var identity = CreateIdentity(
            (contextA, "Hero A", 5_000),
            (contextB, "Hero B", 9_000));
        var viewed = new TestGameplaySessionContextSupport.FakeViewedContextService(
            TestGameplaySessionContextSupport.FollowingLive(contextA));
        var resolver = new GameplaySessionContextResolver(identity, viewed);
        using var live = CreateLiveSession(identity, viewed, resolver);
        DrainDispatcher();

        live.ClearSessionCommand.Execute(null);
        DrainDispatcher();
        Assert.Equal("0", live.SessionExperienceLabel);

        live.SelectSessionTabCommand.Execute(live.SessionTabs.Single(tab => tab.ContextId == contextB));
        DrainDispatcher();
        Assert.Equal(Format(9_000), live.SessionExperienceLabel);

        live.SelectSessionTabCommand.Execute(live.SessionTabs.Single(tab => tab.ContextId == contextA));
        DrainDispatcher();
        Assert.Equal("0", live.SessionExperienceLabel);
    }

    [Fact]
    public void Unresolved_character_context_receives_selectable_tab()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var unresolved = TestGameplaySessionContextSupport.CreateContext(
                contextB, "acct-primary", null, string.Empty, startedAt, experience: 500) with
        {
            CharacterDisplayName = null,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Unknown,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Unresolved,
            IdentityStatusLabel = "Unknown",
            IdentityDetail = "Character identity unresolved",
            AccountDisplayName = "TestAccount"
        };

        var identity = new FakeGameplaySessionIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(
                    contextA, "acct-1", recordA, "Hero A", startedAt, experience: 1_000),
                unresolved)
        };
        var viewed = new TestGameplaySessionContextSupport.FakeViewedContextService(
            TestGameplaySessionContextSupport.FollowingLive(contextA));
        var resolver = new GameplaySessionContextResolver(identity, viewed);
        using var live = CreateLiveSession(identity, viewed, resolver);
        DrainDispatcher();

        var unresolvedTab = live.SessionTabs.Single(tab => tab.ContextId == contextB);
        Assert.Equal("Character Unknown", unresolvedTab.PrimaryLabel);
        Assert.Equal("TestAccount", unresolvedTab.SecondaryLabel);

        live.SelectSessionTabCommand.Execute(unresolvedTab);
        DrainDispatcher();

        Assert.Equal(Format(500), live.SessionExperienceLabel);
    }

    [Fact]
    public void Selected_session_ending_with_one_remaining_selects_remaining_session()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeGameplaySessionIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(
                    contextA, "acct-1", recordA, "Hero A", startedAt, experience: 1_000),
                TestGameplaySessionContextSupport.CreateContext(
                    contextB, "acct-2", recordB, "Hero B", startedAt, experience: 2_000))
        };

        var repository = CreateRepository();
        var viewed = new ViewedContextService(identity, repository);
        var resolver = new GameplaySessionContextResolver(identity, viewed);
        using var live = CreateLiveSession(identity, viewed, resolver);
        DrainDispatcher();
        viewed.SelectGameplaySessionContext(contextA);
        DrainDispatcher();

        identity.Current = Snapshot(
            TestGameplaySessionContextSupport.CreateContext(
                contextB, "acct-2", recordB, "Hero B", startedAt, experience: 2_000));
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(contextB, viewed.Current.LiveFollowContextId);
        Assert.Equal(Format(2_000), live.SessionExperienceLabel);
        Assert.False(live.ShowSessionTabs);
    }

  [Fact]
    public void Unselected_session_ending_does_not_change_selection()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeGameplaySessionIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(
                    contextA, "acct-1", recordA, "Hero A", startedAt, experience: 1_000),
                TestGameplaySessionContextSupport.CreateContext(
                    contextB, "acct-2", recordB, "Hero B", startedAt, experience: 2_000))
        };

        var repository = CreateRepository();
        var viewed = new ViewedContextService(identity, repository);
        var resolver = new GameplaySessionContextResolver(identity, viewed);
        using var live = CreateLiveSession(identity, viewed, resolver);
        DrainDispatcher();

        identity.Current = Snapshot(
            TestGameplaySessionContextSupport.CreateContext(
                contextA, "acct-1", recordA, "Hero A", startedAt, experience: 1_000));
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(contextA, viewed.Current.LiveFollowContextId);
        Assert.Equal(Format(1_000), live.SessionExperienceLabel);
        Assert.False(live.ShowSessionTabs);
    }

    private static LiveSessionViewModel CreateLiveSession(
        IGameplaySessionIdentityReadService identity,
        ViewedContextState viewed) =>
        TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity, viewed);

    private static LiveSessionViewModel CreateLiveSession(
        IGameplaySessionIdentityReadService identity,
        IViewedContextService viewed,
        IGameplaySessionContextResolver resolver) =>
        new(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off },
            new TestGameplaySessionContextSupport.FakeGameplaySessionManager(),
            identity,
            resolver,
            viewed);

    private static AnalyticsViewModel CreateAnalytics(
        IGameplaySessionIdentityReadService identity,
        IViewedContextService viewed,
        IGameplaySessionContextResolver resolver) =>
        new(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off },
            identity,
            resolver,
            viewed);

    private static FakeGameplaySessionIdentityReadService CreateIdentity(
        MonitoringContextId contextId,
        string displayName,
        long experience)
    {
        var identity = new FakeGameplaySessionIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(
                    contextId,
                    "acct-1",
                    CharacterRecordId.CreateNew(),
                    displayName,
                    DateTimeOffset.UtcNow,
                    experience: experience))
        };
        return identity;
    }

    private static FakeGameplaySessionIdentityReadService CreateIdentity(
        params (MonitoringContextId ContextId, string DisplayName, long Experience)[] contexts)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var models = contexts
            .Select((entry, index) => TestGameplaySessionContextSupport.CreateContext(
                entry.ContextId,
                $"acct-{index + 1}",
                CharacterRecordId.CreateNew(),
                entry.DisplayName,
                startedAt,
                experience: entry.Experience))
            .ToArray();
        return new FakeGameplaySessionIdentityReadService
        {
            Current = Snapshot(models)
        };
    }

    private static ViewedContextState Following(MonitoringContextId contextId) =>
        TestGameplaySessionContextSupport.FollowingLive(contextId);

    private static CharacterRepository CreateRepository()
    {
        var path = Path.Combine(Path.GetTempPath(), "coh-analytics-multi-context", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return new CharacterRepository(new CharacterRepositoryOptions { DataDirectory = path });
    }

    private static string Format(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static GameplaySessionIdentityReadModelSnapshot Snapshot(
        params LiveMonitoringContextIdentityReadModel[] contexts) =>
        GameplaySessionIdentityReadModelSnapshot.Create(
            contexts,
            DateTimeOffset.UtcNow,
            revision: contexts.Length);

    private static void DrainDispatcher()
    {
        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }
    }

    private sealed class FakeGameplaySessionIdentityReadService : IGameplaySessionIdentityReadService
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

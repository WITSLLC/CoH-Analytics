using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class LiveSessionContextResolutionTests
{
    [Fact]
    public void Live_session_uses_resolver_instead_of_first_active_context()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(
                    contextA,
                    "acct-1",
                    recordA,
                    "Hero A",
                    startedAt,
                    experience: 1_000,
                    influence: 100),
                TestGameplaySessionContextSupport.CreateContext(
                    contextB,
                    "acct-2",
                    recordB,
                    "Hero B",
                    startedAt,
                    experience: 9_000,
                    influence: 900))
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            TestGameplaySessionContextSupport.FollowingLive(contextB));
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
        Assert.Equal(Format(9_000), viewModel.SessionExperienceLabel);
        Assert.Equal(Format(900), viewModel.SessionGameplayInfluenceLabel);
    }

    [Fact]
    public void Live_session_pinned_context_projects_all_telemetry_from_selected_context()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(
                    contextA,
                    "acct-1",
                    recordA,
                    "Hero A",
                    startedAt,
                    experience: 2_500,
                    influence: 250,
                    salvage: [new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 4 }]),
                TestGameplaySessionContextSupport.CreateContext(
                    contextB,
                    "acct-2",
                    recordB,
                    "Hero B",
                    startedAt,
                    experience: 8_000,
                    influence: 800,
                    salvage: [new GameplaySessionItemTotal { DisplayName = "Luck Charm", Quantity = 9 }]))
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            TestGameplaySessionContextSupport.FollowingLive(contextA));
        DrainDispatcher();

        Assert.True(viewModel.SessionTabs.Single(context => context.ContextId == contextA).IsSelected);
        Assert.False(viewModel.SessionTabs.Single(context => context.ContextId == contextB).IsSelected);
        Assert.Equal(Format(2_500), viewModel.SessionExperienceLabel);
        Assert.Equal(Format(250), viewModel.SessionGameplayInfluenceLabel);
        Assert.Single(viewModel.SalvageDrops);
        Assert.Equal("Fortune", viewModel.SalvageDrops[0].DisplayName);
        Assert.Equal("4", viewModel.SalvageDrops[0].QuantityLabel);
    }

    [Fact]
    public void Live_session_remains_stable_when_snapshot_refreshes_for_follow_target()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var startedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                [
                    TestGameplaySessionContextSupport.CreateContext(
                        contextA,
                        "acct-1",
                        recordA,
                        "Hero A",
                        startedAt,
                        experience: 1_000),
                    TestGameplaySessionContextSupport.CreateContext(
                        contextB,
                        "acct-2",
                        recordB,
                        "Hero B",
                        startedAt,
                        experience: 2_000)
                ],
                revision: 1)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            TestGameplaySessionContextSupport.FollowingLive(contextB));
        DrainDispatcher();
        Assert.Equal(Format(2_000), viewModel.SessionExperienceLabel);

        identity.Current = Snapshot(
            [
                TestGameplaySessionContextSupport.CreateContext(
                    contextA,
                    "acct-1",
                    recordA,
                    "Hero A",
                    startedAt,
                    experience: 9_999),
                TestGameplaySessionContextSupport.CreateContext(
                    contextB,
                    "acct-2",
                    recordB,
                    "Hero B",
                    startedAt,
                    experience: 3_000)
            ],
            revision: 2);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(Format(3_000), viewModel.SessionExperienceLabel);
    }

    private static string Format(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static GameplaySessionIdentityReadModelSnapshot Snapshot(
        params LiveMonitoringContextIdentityReadModel[] contexts) =>
        Snapshot(contexts, revision: 1);

    private static GameplaySessionIdentityReadModelSnapshot Snapshot(
        LiveMonitoringContextIdentityReadModel[] contexts,
        long revision) =>
        GameplaySessionIdentityReadModelSnapshot.Create(
            contexts,
            DateTimeOffset.UtcNow,
            revision);

    private static void DrainDispatcher()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.Invoke(DispatcherPriority.Background, static () => { });
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

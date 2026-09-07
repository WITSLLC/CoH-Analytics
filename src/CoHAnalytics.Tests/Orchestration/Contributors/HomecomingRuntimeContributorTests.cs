using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contributors;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using SharedFakeGameRuntimeService = CoHAnalytics.Tests.Services.FakeGameRuntimeService;

namespace CoHAnalytics.Tests.Orchestration.Contributors;

public sealed class HomecomingRuntimeContributorTests
{
    [Fact]
    public void Descriptor_matches_governing_architecture()
    {
        using var contributor = new HomecomingRuntimeContributor(new FakeGameRuntimeService());

        Assert.Equal(ApplicationProviders.HomecomingRuntime, contributor.Descriptor.ProviderId);
        Assert.Equal([ApplicationCapabilities.HomecomingRuntime], contributor.Descriptor.Produces);
        Assert.Equal([ApplicationCapabilities.HomecomingInstallation], contributor.Descriptor.Requires);
        Assert.Equal(ApplicationContributorImportance.Important, contributor.Descriptor.Importance);
    }

    [Theory]
    [InlineData(GameRuntimeStatus.Unconfigured, 0, ContributorHealth.Unavailable, ContributorActivity.Inactive)]
    [InlineData(GameRuntimeStatus.Off, 0, ContributorHealth.Ready, ContributorActivity.Inactive)]
    [InlineData(GameRuntimeStatus.Running, 2, ContributorHealth.Ready, ContributorActivity.Active)]
    [InlineData(GameRuntimeStatus.Error, 0, ContributorHealth.Error, ContributorActivity.Inactive)]
    public void Maps_runtime_status_honestly(
        GameRuntimeStatus status,
        int clientCount,
        ContributorHealth expectedHealth,
        ContributorActivity expectedActivity)
    {
        var mapped = HomecomingRuntimeContributor.MapStatus(status, clientCount);
        Assert.Equal(expectedHealth, mapped.Health);
        Assert.Equal(expectedActivity, mapped.Activity);
    }

    [Fact]
    public async Task Runtime_status_event_raises_data_free_invalidation()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off };
        using var contributor = new HomecomingRuntimeContributor(runtime);
        var invalidations = 0;
        contributor.ContributionChanged += (_, _) => invalidations++;

        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, 1);
        await Task.Yield();

        Assert.Equal(1, invalidations);
    }

    [Fact]
    public async Task Disposal_unsubscribes_from_runtime_events()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off };
        var contributor = new HomecomingRuntimeContributor(runtime);
        var invalidations = 0;
        contributor.ContributionChanged += (_, _) => invalidations++;

        contributor.Dispose();
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, 1);
        await Task.Yield();

        Assert.Equal(0, invalidations);
    }

    [Fact]
    public async Task Contribution_includes_expected_facts_and_provider_id()
    {
        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClientCount = 1
        };
        using var contributor = new HomecomingRuntimeContributor(runtime);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ApplicationProviders.HomecomingRuntime, contribution.ProviderId);
        Assert.Contains(contribution.Facts, fact => fact.Key == ContributorFactKeys.HomecomingRuntime.ClientCount);
        Assert.Contains(contribution.Facts, fact => fact.Key == ContributorFactKeys.HomecomingRuntime.IsRunning);
        Assert.Contains(contribution.Facts, fact => fact.Key == ContributorFactKeys.HomecomingRuntime.Status);
        Assert.DoesNotContain(contribution.Facts, fact => fact.Key.Contains("character", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeGameRuntimeService : IGameRuntimeService
    {
        public GameRuntimeStatus CurrentStatus { get; set; } = GameRuntimeStatus.Unconfigured;

        public int RunningClientCount { get; set; }

        public IReadOnlyList<HomecomingProcessInstance> RunningClients { get; set; } =
            Array.Empty<HomecomingProcessInstance>();

        public string? LastErrorMessage { get; set; }

        public event EventHandler<GameRuntimeStatusChangedEventArgs>? StatusChanged;

        public void RaiseStatusChanged(
            GameRuntimeStatus previous,
            GameRuntimeStatus next,
            int runningClientCount)
        {
            RaiseStatusChanged(
                previous,
                next,
                SharedFakeGameRuntimeService.CreateUniformClients(runningClientCount));
        }

        public void RaiseStatusChanged(
            GameRuntimeStatus previous,
            GameRuntimeStatus next,
            IReadOnlyList<HomecomingProcessInstance> runningClients)
        {
            var previousClients = RunningClients;
            var previousCount = RunningClientCount;
            CurrentStatus = next;
            RunningClients = runningClients.OrderBy(client => client.ProcessId).ToArray();
            RunningClientCount = RunningClients.Count;
            StatusChanged?.Invoke(
                this,
                new GameRuntimeStatusChangedEventArgs(
                    previous,
                    next,
                    RunningClientCount,
                    previousCount,
                    RunningClients,
                    previousClients));
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LaunchAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}

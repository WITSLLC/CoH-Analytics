using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Diagnostics;

namespace CoHAnalytics.Tests.Orchestration;

public sealed class EventHistoryTests
{
    [Fact]
    public async Task Event_history_trims_to_capacity_while_retaining_monotonic_sequences()
    {
        var manual = new ManualTimeProvider();
        await using var orch = new ApplicationOrchestrator(new ApplicationOrchestratorOptions
        {
            DebounceInterval = TimeSpan.FromMilliseconds(10),
            EventHistoryCapacity = 3,
            TimeProvider = manual,
            CycleValidationMode = CycleValidationMode.Throw
        });
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"])));

        await orch.StartAsync();

        var history = orch.GetEventHistory();
        Assert.Equal(3, history.Count);
        Assert.True(history[0].Sequence > 1);
        Assert.True(history.Zip(history.Skip(1), (first, second) => second.Sequence > first.Sequence).All(value => value));
        Assert.Equal(ApplicationOrchestrationEventKind.RefreshCompleted, history[^1].Kind);
    }

    [Fact]
    public async Task Snapshot_event_records_the_published_revision()
    {
        var manual = new ManualTimeProvider();
        await using var orch = new ApplicationOrchestrator(new ApplicationOrchestratorOptions
        {
            DebounceInterval = TimeSpan.FromMilliseconds(10),
            EventHistoryCapacity = 20,
            TimeProvider = manual,
            CycleValidationMode = CycleValidationMode.Throw
        });
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"])));

        await orch.StartAsync();

        var published = Assert.Single(orch.GetEventHistory(), e =>
            e.Kind == ApplicationOrchestrationEventKind.SnapshotPublished);
        Assert.Equal(orch.Current.Revision, published.SnapshotRevision);
        Assert.Equal("Snapshot 1 published", published.Summary);
    }

    [Fact]
    public async Task Diagnostics_exposes_a_stable_history_snapshot()
    {
        var manual = new ManualTimeProvider();
        await using var orch = new ApplicationOrchestrator(new ApplicationOrchestratorOptions
        {
            DebounceInterval = TimeSpan.FromMilliseconds(10),
            EventHistoryCapacity = 20,
            TimeProvider = manual,
            CycleValidationMode = CycleValidationMode.Throw
        });
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"])));

        var diagnosticHistory = orch.GetDiagnostics().EventHistory;
        await orch.StartAsync();

        Assert.Single(diagnosticHistory);
        Assert.Equal(ApplicationOrchestrationEventKind.ContributorRegistered, diagnosticHistory[0].Kind);
        Assert.True(orch.GetDiagnostics().EventHistory.Count > diagnosticHistory.Count);
    }
}

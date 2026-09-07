using CoHAnalytics.Orchestration.Diagnostics;
using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Orchestration;

public sealed class ApplicationOrchestratorDiagnostics
{
    public IReadOnlyList<ApplicationContributorDescriptor> RegisteredDescriptors { get; init; } = [];

    public IReadOnlyDictionary<string, ApplicationContributorLifecycleState> LifecycleStates { get; init; }
        = new Dictionary<string, ApplicationContributorLifecycleState>(StringComparer.Ordinal);

    public IReadOnlyList<string> TopologicalOrder { get; init; } = [];

    public IReadOnlyDictionary<string, string> CapabilityProducers { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<string> ValidationMessages { get; init; } = [];

    public IReadOnlyList<string> UnresolvedDependencies { get; init; } = [];

    public IReadOnlyDictionary<string, DateTimeOffset?> ContributionTimestamps { get; init; }
        = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);

    public IReadOnlyList<string> StaleProviders { get; init; } = [];

    public IReadOnlyDictionary<string, TimeSpan> LastPullDurations { get; init; }
        = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> ContributorFailureCounts { get; init; }
        = new Dictionary<string, int>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> ContributorTimeoutCounts { get; init; }
        = new Dictionary<string, int>(StringComparer.Ordinal);

    public IReadOnlyList<string> SchemaVersionMismatches { get; init; } = [];

    public long NoOpSuppressionCount { get; init; }

    public string? LatestSnapshotGenerationRule { get; init; }

    public string? PrimaryIssueSelectionRule { get; init; }

    public string? OverallStateSelectionRule { get; init; }

    public IReadOnlyList<ApplicationOrchestrationEvent> EventHistory { get; init; } = [];

    public Exception? LastLifecycleException { get; init; }
}

public sealed class NullApplicationActionRouter : Contracts.IApplicationActionRouter
{
    public static NullApplicationActionRouter Instance { get; } = new();

    public bool CanInvoke(ApplicationAction action) => false;

    public Task<ApplicationActionResult> InvokeAsync(
        ApplicationAction action,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ApplicationActionResult.Failed($"Action '{action.ActionId}' is not routable."));
}

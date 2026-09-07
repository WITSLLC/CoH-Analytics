using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Orchestration.Contributors;

/// <summary>
/// Adapter that publishes aggregate monitoring-context state from <see cref="IMonitoringSessionManager"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per-context detail (account binding, source binding, per-context state) stays in the domain
/// service, which the Live Session workspace will consume directly in a later slice. This
/// contributor summarizes the collection into counted facts only, following the same
/// observation/summary boundary established for <see cref="LogActivityContributor"/> in Slice 4.
/// </para>
/// <para>
/// Lifetime ownership: this contributor starts and stops the wrapped manager through
/// <see cref="IApplicationContributorLifecycle"/>. The composition root constructs and finally
/// disposes the manager but never starts it, so start and stop happen exactly once.
/// </para>
/// </remarks>
public sealed class MonitoringSessionManagerContributor
    : IApplicationContributor, IApplicationContributorLifecycle, IDisposable
{
    private readonly IMonitoringSessionManager _manager;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public MonitoringSessionManagerContributor(
        IMonitoringSessionManager manager,
        TimeProvider? timeProvider = null)
    {
        _manager = manager;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Descriptor = CreateDescriptor();
        _manager.StateChanged += OnStateChanged;
    }

    public ApplicationContributorDescriptor Descriptor { get; }

    public event EventHandler? ContributionChanged;

    public Task<ApplicationContribution> GetContributionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildContribution());
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _manager.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _manager.StopAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manager.StateChanged -= OnStateChanged;
    }

    /// <summary>
    /// The manager event is treated purely as an invalidation hint: the orchestrator pulls
    /// current state rather than receiving a pushed snapshot. This is how a runtime transition
    /// reaches the orchestrator immediately, without waiting for Log Activity's poll.
    /// </summary>
    private void OnStateChanged(object? sender, MonitoringSessionManagerChangedEventArgs e) =>
        ContributionChanged?.Invoke(this, EventArgs.Empty);

    private ApplicationContribution BuildContribution()
    {
        var now = _timeProvider.GetUtcNow();
        var snapshot = _manager.Current;

        // Revision 7 §3.6.20.5: suspension is policy, not failure. Zero contexts, all contexts
        // suspended, and contexts merely waiting for a source are all the same honest state:
        // the manager is healthy and armed, waiting on a precondition. Only a per-context error
        // degrades health, and never to Error at this aggregate level (§15).
        var health = snapshot.ErrorContextCount > 0
            ? ContributorHealth.Degraded
            : ContributorHealth.Ready;

        // Never ReceivingData: that remains Log Activity's claim until parser workers exist
        // (§15). Active only when at least one context is genuinely configured (Ready) and not
        // suspended.
        var activity = snapshot.ActiveContextCount > 0
            ? ContributorActivity.Active
            : ContributorActivity.Waiting;

        return ContributorContributionFactory.Create(
            Descriptor,
            health,
            activity,
            BuildFacts(snapshot, _manager.IsRuntimeAvailable, now),
            BuildIssues(snapshot, now),
            _timeProvider);
    }

    private static IReadOnlyList<ApplicationFact> BuildFacts(
        MonitoringSessionManagerSnapshot snapshot,
        bool isRuntimeAvailable,
        DateTimeOffset observedAt)
    {
        var facts = new List<ApplicationFact>
        {
            Count(ContributorFactKeys.MonitoringSessionManager.ContextCount, snapshot.ContextCount, "Monitoring contexts"),
            Count(ContributorFactKeys.MonitoringSessionManager.ActiveContextCount, snapshot.ActiveContextCount, "Active contexts"),
            Count(ContributorFactKeys.MonitoringSessionManager.SuspendedContextCount, snapshot.SuspendedContextCount, "Suspended contexts"),
            Count(ContributorFactKeys.MonitoringSessionManager.WaitingContextCount, snapshot.WaitingContextCount, "Contexts waiting for a source"),
            Count(ContributorFactKeys.MonitoringSessionManager.ErrorContextCount, snapshot.ErrorContextCount, "Contexts in error"),
            ContributorContributionFactory.BooleanFact(
                ContributorFactKeys.MonitoringSessionManager.RuntimeAvailable,
                isRuntimeAvailable,
                ApplicationFactScope.Provider,
                observedAt,
                display: new ApplicationFactDisplay("Homecoming runtime available")),
            Count(ContributorFactKeys.MonitoringSessionManager.PendingOfferCount, snapshot.PendingOfferCount, "Pending ambiguous-source decisions"),
            Count(ContributorFactKeys.MonitoringSessionManager.ClaimedSourceCount, snapshot.ClaimedSourceCount, "Claimed sources"),
            Count(ContributorFactKeys.MonitoringSessionManager.UnclaimedGrowingSourceCount, snapshot.UnclaimedGrowingSourceCount, "Unclaimed growing sources"),
            Count(ContributorFactKeys.MonitoringSessionManager.AmbiguousSourceCount, snapshot.AmbiguousSourceCount, "Sources awaiting an explicit monitoring choice"),
            Count(ContributorFactKeys.MonitoringSessionManager.DeclinedSourceCount, snapshot.DeclinedSourceCount, "Declined sources currently suppressed"),
            Count(ContributorFactKeys.MonitoringSessionManager.RolloverCount, RolloverCount(snapshot), "Contexts most recently rolled over")
        };

        var lastTransitionAt = LastSourceTransitionAt(snapshot);
        if (lastTransitionAt is not null)
        {
            facts.Add(ContributorContributionFactory.TimestampFact(
                ContributorFactKeys.MonitoringSessionManager.LastSourceTransitionAt,
                lastTransitionAt.Value,
                ApplicationFactScope.Provider,
                observedAt,
                display: new ApplicationFactDisplay("Most recent source transition")));
        }

        return facts;

        ApplicationFact Count(string key, long value, string label) =>
            ContributorContributionFactory.IntegerFact(
                key,
                value,
                ApplicationFactScope.Provider,
                observedAt,
                display: new ApplicationFactDisplay(label));
    }

    private static int RolloverCount(MonitoringSessionManagerSnapshot snapshot) =>
        snapshot.Contexts.Count(context => context.LastSourceTransitionReason == MonitoringContextChangeReason.AutomaticRollover);

    private static DateTimeOffset? LastSourceTransitionAt(MonitoringSessionManagerSnapshot snapshot)
    {
        DateTimeOffset? latest = null;
        foreach (var context in snapshot.Contexts)
        {
            if (context.LastSourceTransitionAt is { } transitionAt && (latest is null || transitionAt > latest))
            {
                latest = transitionAt;
            }
        }

        return latest;
    }

    private static IReadOnlyList<ApplicationIssue> BuildIssues(
        MonitoringSessionManagerSnapshot snapshot,
        DateTimeOffset now)
    {
        var issues = new List<ApplicationIssue>();

        if (snapshot.SuspendedContextCount > 0)
        {
            // Informational and non-actionable: runtime being offline is expected, healthy
            // policy (Revision 7 §3.6.20.5), never an Error or Warning.
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.MonitoringSessionManager.RuntimeUnavailable,
                ApplicationIssueSeverity.Information,
                $"{snapshot.SuspendedContextCount} monitoring context(s) are suspended while the Homecoming client is offline.",
                ApplicationProviders.MonitoringSessionManager,
                now));
        }

        if (snapshot.ErrorContextCount > 0)
        {
            // One aggregate issue rather than one per context, so a bulk failure cannot flood
            // the application with issues (§17).
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.MonitoringSessionManager.ContextError,
                ApplicationIssueSeverity.Warning,
                $"{snapshot.ErrorContextCount} monitoring context(s) reported an error.",
                ApplicationProviders.MonitoringSessionManager,
                now));
        }

        if (snapshot.AmbiguousSourceCount > 0)
        {
            // Genuine user action required: unambiguous sources are enrolled automatically, so a
            // pending offer always means the manager would have to guess (§3.6.6).
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.MonitoringSessionManager.SourceSelectionRequired,
                ApplicationIssueSeverity.Warning,
                $"{snapshot.AmbiguousSourceCount} candidate source(s) require an explicit monitoring choice.",
                ApplicationProviders.MonitoringSessionManager,
                now,
                requiresUserAction: true));
        }

        if (snapshot.ActiveContextCount > 1)
        {
            // Describes what the application is already doing. Concurrent monitoring is normal
            // state, never a prompt to enroll anything (§3.6.6).
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.MonitoringSessionManager.ConcurrentSessionsMonitored,
                ApplicationIssueSeverity.Information,
                $"{snapshot.ActiveContextCount} active Homecoming sessions are being monitored.",
                ApplicationProviders.MonitoringSessionManager,
                now));
        }

        var unresolvedSourceLossCount = snapshot.Contexts.Count(context =>
            context.SourceLostAt is not null && context.State == MonitoringContextState.WaitingForSource);
        if (unresolvedSourceLossCount > 0)
        {
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.MonitoringSessionManager.SourceUnavailable,
                ApplicationIssueSeverity.Warning,
                $"{unresolvedSourceLossCount} monitoring context(s) lost their claimed source.",
                ApplicationProviders.MonitoringSessionManager,
                now,
                requiresUserAction: true));
        }

        return issues;
    }

    private static ApplicationContributorDescriptor CreateDescriptor() =>
        new(
            ApplicationProviders.MonitoringSessionManager,
            "Monitoring Sessions",
            [ApplicationCapabilities.MonitoringContexts, ApplicationCapabilities.MonitoringSourceSelection],
            [ApplicationCapabilities.LogDiscovery, ApplicationCapabilities.LogActivity, ApplicationCapabilities.AccountDiscovery],
            [ApplicationCapabilities.HomecomingRuntime],
            ApplicationContributorImportance.Important,
            45)
        {
            Description = "Coordinates active Homecoming monitoring contexts and selected log sources.",
            IconKey = "monitoring",
            SchemaVersion = ApplicationContributorDescriptor.ExpectedSchemaVersion
        };
}

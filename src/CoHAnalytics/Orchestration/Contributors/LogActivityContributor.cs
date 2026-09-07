using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Orchestration.Contributors;

/// <summary>
/// Adapter that publishes aggregate chat-log observation state from <see cref="ILogActivityService"/>.
/// </summary>
/// <remarks>
/// <para>
/// The contributor summarizes the candidate collection into counted facts. Detailed per-source
/// state deliberately stays in the domain service, which the future monitoring session manager
/// consumes directly; flattening candidates into orchestration facts would duplicate the domain
/// snapshot and blur the ownership boundary between observation and source assignment.
/// </para>
/// <para>
/// Lifetime ownership: this contributor starts and stops the wrapped service through
/// <see cref="IApplicationContributorLifecycle"/>. The composition root constructs and finally
/// disposes the service but never starts it, so start and stop happen exactly once.
/// </para>
/// </remarks>
public sealed class LogActivityContributor
    : IApplicationContributor, IApplicationContributorLifecycle, IDisposable
{
    /// <summary>
    /// Above this many significant unavailable sources, one summary issue is reported instead of
    /// one issue per source, so a bulk loss cannot flood the application with issues.
    /// </summary>
    private const int MaxIndividualSourceIssues = 3;

    private readonly ILogActivityService _logActivityService;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public LogActivityContributor(
        ILogActivityService logActivityService,
        TimeProvider? timeProvider = null)
    {
        _logActivityService = logActivityService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Descriptor = CreateDescriptor();
        _logActivityService.ActivityChanged += OnActivityChanged;
    }

    public ApplicationContributorDescriptor Descriptor { get; }

    public event EventHandler? ContributionChanged;

    public Task<ApplicationContribution> GetContributionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildContribution());
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _logActivityService.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _logActivityService.StopAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logActivityService.ActivityChanged -= OnActivityChanged;
    }

    /// <summary>
    /// The service event is treated purely as an invalidation hint: the orchestrator pulls
    /// current state rather than receiving a pushed snapshot.
    /// </summary>
    private void OnActivityChanged(object? sender, LogActivityChangedEventArgs e) =>
        ContributionChanged?.Invoke(this, EventArgs.Empty);

    private ApplicationContribution BuildContribution()
    {
        var now = _timeProvider.GetUtcNow();
        var snapshot = _logActivityService.Current;

        if (_logActivityService.LastScanFailureMessage is { } failure)
        {
            return ContributorContributionFactory.Create(
                Descriptor,
                ContributorHealth.Error,
                ContributorActivity.Inactive,
                BuildFacts(snapshot, now),
                [
                    ContributorContributionFactory.Issue(
                        ContributorIssueCodes.LogActivity.ObservationFailed,
                        ApplicationIssueSeverity.Error,
                        "Chat-log observation failed.",
                        Descriptor.ProviderId,
                        now,
                        detail: failure)
                ],
                _timeProvider);
        }

        var significantLosses = GetSignificantLosses(snapshot);
        var health = significantLosses.Count > 0
            ? ContributorHealth.Degraded
            : ContributorHealth.Ready;

        // ReceivingData requires a directly observed length increase during this run. Historical
        // files, recent timestamps, and a running client are never sufficient.
        var activity = snapshot.GrowingCount > 0
            ? ContributorActivity.ReceivingData
            : ContributorActivity.Waiting;

        return ContributorContributionFactory.Create(
            Descriptor,
            health,
            activity,
            BuildFacts(snapshot, now),
            BuildLossIssues(significantLosses, now),
            _timeProvider);
    }

    /// <summary>
    /// Unavailable sources worth reporting: one that could not be read, or one that had been
    /// observed growing and then disappeared. A historical file that was merely deleted is not a
    /// problem and produces no issue.
    /// </summary>
    internal static IReadOnlyList<LogSourceCandidate> GetSignificantLosses(LogActivitySnapshot snapshot) =>
        [.. snapshot.Candidates.Where(candidate =>
            candidate.ActivityState == LogSourceActivityState.Unavailable
            && (candidate.LastChangeKind == LogSourceChangeKind.Inaccessible
                || candidate.HasObservedGrowth))];

    private IReadOnlyList<ApplicationIssue> BuildLossIssues(
        IReadOnlyList<LogSourceCandidate> losses,
        DateTimeOffset now)
    {
        if (losses.Count == 0)
        {
            return [];
        }

        if (losses.Count > MaxIndividualSourceIssues)
        {
            return
            [
                ContributorContributionFactory.Issue(
                    ContributorIssueCodes.LogActivity.SourceUnavailable,
                    ApplicationIssueSeverity.Warning,
                    $"{losses.Count} observed chat-log sources are currently unavailable.",
                    Descriptor.ProviderId,
                    now)
            ];
        }

        return
        [
            .. losses.Select(candidate => ContributorContributionFactory.Issue(
                ContributorIssueCodes.LogActivity.SourceUnavailable,
                ApplicationIssueSeverity.Warning,
                "An observed chat-log source is currently unavailable.",
                Descriptor.ProviderId,
                now,
                detail: candidate.UnavailableReason,
                relatedEntityId: candidate.SourceId.Value))
        ];
    }

    private static IReadOnlyList<ApplicationFact> BuildFacts(
        LogActivitySnapshot snapshot,
        DateTimeOffset observedAt)
    {
        var facts = new List<ApplicationFact>
        {
            Count(ContributorFactKeys.LogActivity.AccountCount, snapshot.ObservedAccountCount, "Accounts observed"),
            Count(ContributorFactKeys.LogActivity.LogsFolderCount, snapshot.LogsFolderCount, "Logs folders found"),
            Count(ContributorFactKeys.LogActivity.CandidateCount, snapshot.CandidateCount, "Candidate log sources"),
            Count(ContributorFactKeys.LogActivity.GrowingCount, snapshot.GrowingCount, "Sources observed growing"),
            Count(ContributorFactKeys.LogActivity.InactiveCount, snapshot.InactiveCount, "Sources with no recent growth"),
            Count(ContributorFactKeys.LogActivity.HistoricalCount, snapshot.HistoricalCount, "Historical sources"),
            Count(ContributorFactKeys.LogActivity.UnavailableCount, snapshot.UnavailableCount, "Unavailable sources"),
            Count(
                ContributorFactKeys.LogActivity.RolloverCandidateCount,
                snapshot.RolloverCandidateCount,
                "Likely daily rollover candidates")
        };

        if (snapshot.LastGrowthAt is { } lastGrowth)
        {
            facts.Add(ContributorContributionFactory.TimestampFact(
                ContributorFactKeys.LogActivity.LastGrowthAt,
                lastGrowth,
                ApplicationFactScope.Provider,
                observedAt,
                display: new ApplicationFactDisplay("Last observed log growth")));
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

    private static ApplicationContributorDescriptor CreateDescriptor() =>
        new(
            ApplicationProviders.LogActivity,
            "Chat Log Activity",
            [ApplicationCapabilities.LogDiscovery, ApplicationCapabilities.LogActivity],
            [ApplicationCapabilities.AccountDiscovery],
            [ApplicationCapabilities.HomecomingRuntime],
            ApplicationContributorImportance.Important,
            40)
        {
            Description = "Observes Homecoming account chat-log files and reports candidate activity.",
            IconKey = "monitoring",
            SchemaVersion = ApplicationContributorDescriptor.ExpectedSchemaVersion
        };
}

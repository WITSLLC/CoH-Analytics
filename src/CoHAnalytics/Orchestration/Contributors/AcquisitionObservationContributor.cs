using CoHAnalytics.Observations;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Orchestration.Contributors;

/// <summary>Reports optional acquisition observation capture health without affecting core application status.</summary>
public sealed class AcquisitionObservationContributor : IApplicationContributor, IDisposable
{
    private readonly IAcquisitionObservationService _observationService;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public AcquisitionObservationContributor(
        IAcquisitionObservationService observationService,
        TimeProvider? timeProvider = null)
    {
        _observationService = observationService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Descriptor = CreateDescriptor();
        _observationService.Changed += OnObservationChanged;
    }

    public ApplicationContributorDescriptor Descriptor { get; }

    public event EventHandler? ContributionChanged;

    public Task<ApplicationContribution> GetContributionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildContribution());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _observationService.Changed -= OnObservationChanged;
    }

    private void OnObservationChanged(object? sender, EventArgs e) =>
        ContributionChanged?.Invoke(this, EventArgs.Empty);

    private ApplicationContribution BuildContribution()
    {
        var now = _timeProvider.GetUtcNow();
        var status = _observationService.GetStatus();
        var facts = new List<ApplicationFact>
        {
            ContributorContributionFactory.BooleanFact(
                "observations.capture.active",
                status.Health is AcquisitionCaptureHealth.Active,
                ApplicationFactScope.Provider,
                now,
                display: new ApplicationFactDisplay("Capture Active", "Capture Active")),
            ContributorContributionFactory.IntegerFact(
                "observations.acquisitions.seen",
                status.AcquisitionsSeen,
                ApplicationFactScope.Provider,
                now,
                display: new ApplicationFactDisplay("Acquisitions Seen", "Seen")),
            ContributorContributionFactory.IntegerFact(
                "observations.acquisitions.resolved",
                status.AcquisitionsResolved,
                ApplicationFactScope.Provider,
                now,
                display: new ApplicationFactDisplay("Acquisitions Resolved", "Resolved")),
            ContributorContributionFactory.IntegerFact(
                "observations.acquisitions.unresolved",
                status.AcquisitionsNeedingClassification,
                ApplicationFactScope.Provider,
                now,
                display: new ApplicationFactDisplay("Acquisitions Needing Classification", "Needs Classification")),
            ContributorContributionFactory.IntegerFact(
                "observations.acquisitions.presented_unresolved",
                status.AcquisitionsPresentedUnresolved,
                ApplicationFactScope.Provider,
                now,
                display: new ApplicationFactDisplay("Presentation Known, Catalog Missing", "Catalog Missing")),
            ContributorContributionFactory.IntegerFact(
                "observations.dropped",
                status.DroppedObservations,
                ApplicationFactScope.Provider,
                now,
                display: new ApplicationFactDisplay("Dropped Observations", "Dropped"))
        };

        if (status.LastCaptureAtUtc is not null)
        {
            facts.Add(ContributorContributionFactory.TimestampFact(
                "observations.last_capture_at",
                status.LastCaptureAtUtc.Value,
                ApplicationFactScope.Provider,
                now,
                display: new ApplicationFactDisplay("Last Capture", "Last Capture")));
        }

        if (!string.IsNullOrWhiteSpace(status.CatalogVersion))
        {
            facts.Add(ContributorContributionFactory.TextFact(
                "observations.catalog_version",
                status.CatalogVersion,
                ApplicationFactScope.Provider,
                now,
                display: new ApplicationFactDisplay("Catalog Version", "Catalog Version")));
        }

        var health = status.Health switch
        {
            AcquisitionCaptureHealth.Active => ContributorHealth.Ready,
            AcquisitionCaptureHealth.Degraded => ContributorHealth.Degraded,
            _ => ContributorHealth.Unavailable
        };

        var activity = status.AcquisitionsSeen > 0 || status.LastCaptureAtUtc is not null
            ? ContributorActivity.Active
            : ContributorActivity.Inactive;

        var issues = new List<ApplicationIssue>();
        if (status.Health is AcquisitionCaptureHealth.Degraded
            && !string.IsNullOrWhiteSpace(status.HealthDetail))
        {
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.Observations.PersistenceFailed,
                ApplicationIssueSeverity.Warning,
                "Acquisition observation capture is degraded.",
                Descriptor.ProviderId,
                now,
                detail: status.HealthDetail));
        }

        if (status.Health is AcquisitionCaptureHealth.Unavailable)
        {
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.Observations.CaptureUnavailable,
                ApplicationIssueSeverity.Warning,
                "Acquisition observation capture is unavailable.",
                Descriptor.ProviderId,
                now,
                detail: status.HealthDetail));
        }

        return ContributorContributionFactory.Create(
            Descriptor,
            health,
            activity,
            facts,
            issues,
            _timeProvider);
    }

    private static ApplicationContributorDescriptor CreateDescriptor() =>
        new(
            ApplicationProviders.Observations,
            "Acquisition Observations",
            [],
            [],
            [],
            ApplicationContributorImportance.Optional,
            90);
}

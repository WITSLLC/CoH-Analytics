using CoHAnalytics.Models;

namespace CoHAnalytics.Observations;

/// <summary>Acquisition observation capture, queue-query, and reconciliation surface.</summary>
public interface IAcquisitionObservationService : IObservationSink
{
    event EventHandler? Changed;

    AcquisitionObservationStatus GetStatus();

    IReadOnlyList<AcquisitionObservationListItem> GetNeedsClassification(
        AcquisitionObservationFilter? filter = null) => [];

    AcquisitionObservationDetail? GetObservationDetail(string normalizedKey) => null;

    AcquisitionObservationOperationResult ReconcileObservation(string normalizedKey) =>
        new() { Outcome = AcquisitionObservationOperationOutcome.NotFound };

    void RecordClassificationAttempt(bool resolved);

    void RecordClassificationAttempt(AcquisitionIdentityResolutionState resolutionState) =>
        RecordClassificationAttempt(resolutionState is AcquisitionIdentityResolutionState.Resolved);
}

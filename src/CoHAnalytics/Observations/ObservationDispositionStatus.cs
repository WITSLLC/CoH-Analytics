namespace CoHAnalytics.Observations;

/// <summary>Developer-owned disposition for an aggregated observation identity.</summary>
public enum ObservationDispositionStatus
{
    New,
    UnderReview,
    Deferred,
    Rejected,
    Ignored,
    Authored,
    Resolved
}

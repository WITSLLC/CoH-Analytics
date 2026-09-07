namespace CoHAnalytics.Observations;

/// <summary>Stable producer and kind identifiers for acquisition observations.</summary>
public static class AcquisitionObservationConstants
{
    public const string ProducerId = "observations.acquisitions";

    public const string ObservationKind = "UnresolvedAcquisition";

    public const int MaxEvidenceSamplesPerKey = 5;
}

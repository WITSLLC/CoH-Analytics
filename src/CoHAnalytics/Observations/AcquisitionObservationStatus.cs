namespace CoHAnalytics.Observations;

/// <summary>Runtime capture health and counters for the Diagnostics surface.</summary>
public sealed record AcquisitionObservationStatus
{
    public AcquisitionCaptureHealth Health { get; init; } = AcquisitionCaptureHealth.Unavailable;

    public string? HealthDetail { get; init; }

    public long AcquisitionsSeen { get; init; }

    public long AcquisitionsResolved { get; init; }

    public long AcquisitionsPresentedUnresolved { get; init; }

    public long AcquisitionsUnresolved { get; init; }

    public long AcquisitionsNeedingClassification =>
        AcquisitionsPresentedUnresolved + AcquisitionsUnresolved;

    public long DroppedObservations { get; init; }

    public DateTimeOffset? LastCaptureAtUtc { get; init; }

    public string? CatalogVersion { get; init; }

    public string ObservationsRoot { get; init; } = string.Empty;
}

public enum AcquisitionCaptureHealth
{
    Active,
    Degraded,
    Unavailable
}

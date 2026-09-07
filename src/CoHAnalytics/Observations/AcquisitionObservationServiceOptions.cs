namespace CoHAnalytics.Observations;

public sealed class AcquisitionObservationServiceOptions
{
    public int QueueCapacity { get; init; } = AcquisitionObservationService.DefaultQueueCapacity;

    public bool EnableBackgroundWriter { get; init; } = true;

    internal Action? BeforeEvidenceAppend { get; init; }
}

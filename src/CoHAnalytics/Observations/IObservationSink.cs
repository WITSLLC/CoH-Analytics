namespace CoHAnalytics.Observations;

/// <summary>Non-blocking observation capture sink used by classification decorators.</summary>
public interface IObservationSink
{
    /// <summary>Attempts to enqueue an unresolved observation without blocking the caller.</summary>
    bool TryRecordUnresolvedAcquisition(AcquisitionObservationRecord observation);
}

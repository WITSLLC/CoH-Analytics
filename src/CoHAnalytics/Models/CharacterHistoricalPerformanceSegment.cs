namespace CoHAnalytics.Models;

/// <summary>
/// One retained historical performance observation and its metrics, resolved for a canonical
/// character identity. Inclusion controls Overview aggregation but never segment enumeration.
/// </summary>
public sealed record CharacterHistoricalPerformanceSegment
{
    public required CharacterPerformanceObservation Observation { get; init; }

    public required CharacterRecordId CanonicalCharacterRecordId { get; init; }

    public required CharacterHistoricalPerformanceSnapshot Metrics { get; init; }
}

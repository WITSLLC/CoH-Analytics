using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Reads lifetime historical performance by durable character identity.</summary>
public interface ICharacterHistoricalPerformanceReadService
{
    event EventHandler? Changed;

    CharacterHistoricalPerformanceSnapshot GetLifetime(CharacterRecordId characterRecordId);

    /// <summary>
    /// Returns every retained observation for the canonical character family, including segments
    /// excluded from Overview aggregation, in deterministic newest-first order.
    /// </summary>
    IReadOnlyList<CharacterHistoricalPerformanceSegment> GetSegments(
        CharacterRecordId characterRecordId);
}

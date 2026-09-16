using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Production historical reader over Slice 9 durable Segments and legacy v1/v2 observations.
/// Does not parse logs, replay <c>CombatEngine</c>, or consult the current catalog/build.
/// </summary>
public interface IHistoricalSegmentReader
{
    IReadOnlyList<HistoricalSegmentHeader> ListHeaders(HistoricalSegmentQuery? query = null);

    HistoricalLoadResult TryLoad(string segmentId, HistoricalLoadOptions? options = null);

    HistoricalLoadResult TryLoad(
        GameplaySessionId gameplaySessionId,
        int segmentOrdinal,
        HistoricalLoadOptions? options = null);
}

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

    /// <summary>
    /// Removes the published Segment directory and matching observation for this capture key.
    /// Does not touch source chat logs.
    /// </summary>
    SegmentDeleteResult Delete(GameplaySessionId gameplaySessionId, int segmentOrdinal);
}

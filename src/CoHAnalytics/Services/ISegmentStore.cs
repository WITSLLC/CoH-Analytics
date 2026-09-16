using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Durable hybrid Segment publication. Test roundtrips are allowed; no historical UI reader.</summary>
public interface ISegmentStore
{
    string SegmentsDirectory { get; }

    string ManifestsDirectory { get; }

    SegmentPersistResult Persist(SegmentDraft draft);

    SegmentLoadResult TryLoad(GameplaySessionId gameplaySessionId, int segmentOrdinal);
}

internal sealed class NullSegmentStore : ISegmentStore
{
    public static NullSegmentStore Instance { get; } = new();

    public string SegmentsDirectory => string.Empty;

    public string ManifestsDirectory => string.Empty;

    public SegmentPersistResult Persist(SegmentDraft draft) =>
        new() { Outcome = SegmentPersistOutcome.Skipped };

    public SegmentLoadResult TryLoad(GameplaySessionId gameplaySessionId, int segmentOrdinal) =>
        new() { Outcome = SegmentLoadOutcome.NotFound };
}

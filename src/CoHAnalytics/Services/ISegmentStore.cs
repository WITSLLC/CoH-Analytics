using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Durable hybrid Segment publication and low-level published-file reads.</summary>
public interface ISegmentStore
{
    string SegmentsDirectory { get; }

    string ManifestsDirectory { get; }

    event EventHandler<SegmentPublishedEventArgs>? SegmentPublished;

    SegmentPersistResult Persist(SegmentDraft draft);

    SegmentDeleteResult Delete(GameplaySessionId gameplaySessionId, int segmentOrdinal);

    SegmentLoadResult TryLoad(GameplaySessionId gameplaySessionId, int segmentOrdinal);

    SegmentLoadResult TryLoad(
        GameplaySessionId gameplaySessionId,
        int segmentOrdinal,
        SegmentLoadOptions options);

    SegmentPublishedHeader ReadHeader(GameplaySessionId gameplaySessionId, int segmentOrdinal);

    IReadOnlyList<SegmentPublishedHeader> ListHeaders();
}

public sealed class SegmentPublishedEventArgs : EventArgs
{
    public required GameplaySessionId GameplaySessionId { get; init; }

    public required int SegmentOrdinal { get; init; }
}

internal sealed class NullSegmentStore : ISegmentStore
{
    public static NullSegmentStore Instance { get; } = new();

    public string SegmentsDirectory => string.Empty;

    public string ManifestsDirectory => string.Empty;

    public event EventHandler<SegmentPublishedEventArgs>? SegmentPublished
    {
        add { }
        remove { }
    }

    public SegmentPersistResult Persist(SegmentDraft draft) =>
        new() { Outcome = SegmentPersistOutcome.Skipped };

    public SegmentDeleteResult Delete(GameplaySessionId gameplaySessionId, int segmentOrdinal) =>
        new() { Outcome = SegmentDeleteOutcome.NotFound };

    public SegmentLoadResult TryLoad(GameplaySessionId gameplaySessionId, int segmentOrdinal) =>
        new() { Outcome = SegmentLoadOutcome.NotFound };

    public SegmentLoadResult TryLoad(
        GameplaySessionId gameplaySessionId,
        int segmentOrdinal,
        SegmentLoadOptions options) =>
        new() { Outcome = SegmentLoadOutcome.NotFound };

    public SegmentPublishedHeader ReadHeader(GameplaySessionId gameplaySessionId, int segmentOrdinal) =>
        new()
        {
            Status = SegmentHeaderReadStatus.NotFound,
            SegmentId = SegmentCaptureKey.Format(gameplaySessionId, segmentOrdinal)
        };

    public IReadOnlyList<SegmentPublishedHeader> ListHeaders() => [];
}

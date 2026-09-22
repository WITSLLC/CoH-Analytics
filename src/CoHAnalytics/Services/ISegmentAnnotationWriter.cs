using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Updates the existing mutable sidecar of a published durable segment.</summary>
public interface ISegmentAnnotationWriter
{
    SegmentPersistResult TryUpdateAnnotations(
        GameplaySessionId gameplaySessionId, int segmentOrdinal, SegmentAnnotations annotations);
}

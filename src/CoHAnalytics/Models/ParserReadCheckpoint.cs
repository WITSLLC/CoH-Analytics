namespace CoHAnalytics.Models;

/// <summary>Runtime-only parser checkpoint for one active source segment.</summary>
public sealed record ParserReadCheckpoint
{
    public required ParserSourceSegmentId SourceSegmentId { get; init; }

    public required long ByteOffset { get; init; }

    public required int PartialBufferByteCount { get; init; }

    public required bool HasPendingBomProbe { get; init; }
}

namespace CoHAnalytics.Models;

/// <summary>
/// Scoped occurrence identity. Parser sequence is never a standalone global key; it is always
/// interpreted with source, source-segment, and binding generation.
/// </summary>
public sealed record EventOccurrenceRef
{
    public required string SourceId { get; init; }

    public required Guid SourceSegmentId { get; init; }

    public required long BindingGeneration { get; init; }

    public required long ParserSequence { get; init; }
}

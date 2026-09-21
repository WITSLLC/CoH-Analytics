namespace CoHAnalytics.Models;

/// <summary>
/// One complete, context-tagged raw source line. Slice 6B deliberately performs no gameplay
/// interpretation; unknown complete lines use this same envelope.
/// </summary>
public sealed record ParserRawEvent
{
    public required MonitoringContextId ContextId { get; init; }

    public required LogSourceId SourceId { get; init; }

    public required ParserSourceSegmentId SourceSegmentId { get; init; }

    public required long BindingGeneration { get; init; }

    /// <summary>Source-binding transition that opened the segment emitting this line.</summary>
    public MonitoringSourceTransitionKind SourceTransitionKind { get; init; } =
        MonitoringSourceTransitionKind.None;

    public required long Sequence { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public required string RawLine { get; init; }

    /// <summary>Inclusive source byte offset after any consumed UTF-8 BOM.</summary>
    public required long SourceByteStart { get; init; }

    /// <summary>Exclusive source byte offset including the terminating LF or CRLF bytes.</summary>
    public required long SourceByteEnd { get; init; }

    public required ParserLineStatus LineStatus { get; init; }
}

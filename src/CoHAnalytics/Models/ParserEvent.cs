namespace CoHAnalytics.Models;

/// <summary>Immutable structural classification preserving every byte-level raw provenance field.</summary>
public sealed record ParserEvent
{
    public required MonitoringContextId ContextId { get; init; }
    public required LogSourceId SourceId { get; init; }
    public required ParserSourceSegmentId SourceSegmentId { get; init; }
    public required long BindingGeneration { get; init; }

    /// <summary>Source-binding transition provenance copied from the emitting parser segment.</summary>
    public MonitoringSourceTransitionKind SourceTransitionKind { get; init; } =
        MonitoringSourceTransitionKind.None;

    public required long Sequence { get; init; }

    /// <summary>Application observation time when the line was read; always set for live events.</summary>
    public required DateTimeOffset ObservedAt { get; init; }
    public required string RawLine { get; init; }
    public required long SourceByteStart { get; init; }
    public required long SourceByteEnd { get; init; }
    public required ParserLineStatus LineStatus { get; init; }
    public required ParserEventKind EventKind { get; init; }
    public required ParserClassificationStatus ClassificationStatus { get; init; }
    public required string ClassificationRuleId { get; init; }

    /// <summary>Optional game timestamp parsed from a supported source prefix, with no inferred time zone.</summary>
    public DateTime? SourceTimestamp { get; init; }

    public ParserStructuralEvidence? StructuralEvidence { get; init; }
}

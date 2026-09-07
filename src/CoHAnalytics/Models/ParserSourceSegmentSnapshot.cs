namespace CoHAnalytics.Models;

/// <summary>Immutable parser-owned byte-range record for one logical source segment.</summary>
public sealed record ParserSourceSegmentSnapshot
{
    public required ParserSourceSegmentId SourceSegmentId { get; init; }

    public required MonitoringContextId ContextId { get; init; }

    public required LogSourceId SourceId { get; init; }

    public required long BindingGeneration { get; init; }

    public required MonitoringSourceTransitionKind TransitionKind { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; init; }

    public required long StartingOffset { get; init; }

    public required long EndingOffset { get; init; }

    public required long BytesRead { get; init; }

    public required long LinesEmitted { get; init; }

    public required bool IncompleteFragmentAtEnd { get; init; }

    public required ParserSourceSegmentState State { get; init; }
}

namespace CoHAnalytics.Models;

/// <summary>
/// Capture/arrival clock, not source gameplay elapsed time. Replaying committed provenance and
/// capture bounds reproduces this clock; independently rereading a log need not do so.
/// </summary>
public sealed record SegmentClock
{
    public static SegmentClock Empty { get; } = new();
    public DateTimeOffset? CaptureStartUtc { get; init; }
    /// <summary>Final boundary only; null while the session remains open or suspended.</summary>
    public DateTimeOffset? CaptureEndUtc { get; init; }
    /// <summary>Snapshot observation boundary. No timer-driven publication is implied.</summary>
    public DateTimeOffset? AsOfUtc { get; init; }

    public EventProvenance? FirstObservation { get; init; }
    public EventProvenance? LastObservation { get; init; }
    public DateTimeOffset? FirstObservedAt => FirstObservation?.ObservedAt;
    public DateTimeOffset? LastObservedAt => LastObservation?.ObservedAt;
    /// <summary>Sequence is meaningful only with the corresponding observation's source scope.</summary>
    public long? FirstParserSequence => FirstObservation?.ParserSequence;
    public long? LastParserSequence => LastObservation?.ParserSequence;
    public DateTime? FirstSourceTimestamp => FirstObservation?.SourceTimestamp;
    public DateTime? LastSourceTimestamp => LastObservation?.SourceTimestamp;

    /// <summary>Final end (or explicit AsOfUtc) minus capture start; arrival/session timing only.</summary>
    public Metric<TimeSpan> WallClockDuration { get; init; } = Metric<TimeSpan>.NotCaptured();
    /// <summary>
    /// Last minus first committed ingestion observation. Delays affect this diagnostic span;
    /// it is not a gameplay interval or a rate denominator. Equal times yield an observed zero.
    /// </summary>
    public Metric<TimeSpan> ObservedAnalyticalSpan { get; init; } = Metric<TimeSpan>.NotCaptured();
    /// <summary>No agreed activity-selection policy is implemented in Slice 7.</summary>
    public Metric<TimeSpan> ActiveDuration { get; init; } = Metric<TimeSpan>.Unsupported();
    public Metric<TimeSpan> TrackedPauseAdjustedDuration { get; init; } = Metric<TimeSpan>.NotCaptured();
    /// <summary>
    /// Captured damage hundredths per capture-wall second, truncated to whole hundredths.
    /// Not source-time gameplay DPS or a replacement for CombatSnapshot DPS. Minimum 1 ms.
    /// </summary>
    public Metric<long> WallClockDamagePerSecondHundredths { get; init; } = Metric<long>.NotCaptured();
    public Metric<long> ActiveDamagePerSecondHundredths { get; init; } = Metric<long>.Unsupported();
}

/// <summary>Explicit capture bounds from the session host, never inferred from log timestamps.</summary>
public sealed record SegmentClockCapture
{
    public DateTimeOffset? CaptureStartUtc { get; init; }
    public DateTimeOffset? CaptureEndUtc { get; init; }
    public DateTimeOffset? AsOfUtc { get; init; }
    public TimeSpan? TrackedPauseAdjustedDuration { get; init; }
}

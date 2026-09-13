namespace CoHAnalytics.Models;

/// <summary>Durable parser provenance copied onto a <see cref="CanonicalCombatEvent"/>.</summary>
public sealed record EventProvenance
{
    /// <summary>Current grammar table version carried with canonical events. Not a persistence schema.</summary>
    public const int CurrentGrammarSetVersion = 2;

    public required MonitoringContextId ContextId { get; init; }

    public required string SourceId { get; init; }

    public required string AccountStableId { get; init; }

    public required Guid SourceSegmentId { get; init; }

    public required long BindingGeneration { get; init; }

    public required long ParserSequence { get; init; }

    public required long ByteStart { get; init; }

    public required long ByteEnd { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public DateTime? SourceTimestamp { get; init; }

    public required int GrammarSetVersion { get; init; }

    public required string ClassificationRuleId { get; init; }

    /// <summary>Actual source-channel/message discriminator when present in the log; never inferred.</summary>
    public string? SourceChannel { get; init; }

    public static EventProvenance FromParserEvent(ParserEvent parserEvent) =>
        new()
        {
            ContextId = parserEvent.ContextId,
            SourceId = parserEvent.SourceId.Value,
            AccountStableId = parserEvent.SourceId.AccountStableId,
            SourceSegmentId = parserEvent.SourceSegmentId.Value,
            BindingGeneration = parserEvent.BindingGeneration,
            ParserSequence = parserEvent.Sequence,
            ByteStart = parserEvent.SourceByteStart,
            ByteEnd = parserEvent.SourceByteEnd,
            ObservedAt = parserEvent.ObservedAt,
            SourceTimestamp = parserEvent.SourceTimestamp,
            GrammarSetVersion = CurrentGrammarSetVersion,
            ClassificationRuleId = parserEvent.ClassificationRuleId,
            SourceChannel = parserEvent.SourceChannel
        };
}

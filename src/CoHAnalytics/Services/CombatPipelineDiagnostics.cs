using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Bounded sanitized sample of a combat-shaped line that did not canonicalize.
/// Explanatory metadata only; supported raw filesystem path forms are replaced before retention.
/// </summary>
public sealed record SanitizedUnparsedSample
{
    public required ParserEventKind EventKind { get; init; }

    public required string ClassificationRuleId { get; init; }

    public string? SourceChannel { get; init; }

    public required string SanitizedBody { get; init; }
}

/// <summary>Attribution-mode counts copied from a projection. Correlated remains unused under policy 1.</summary>
public sealed record AttributionModeHistogram
{
    public long Direct { get; init; }

    public long BuildConfirmed { get; init; }

    public long Correlated { get; init; }

    public long Unattributed { get; init; }
}

/// <summary>
/// Observational pipeline diagnostics. Does not change parse, dedup, totals, or persistence.
/// Not a Segment schema document; do not write this into coverage.json.
/// </summary>
public sealed record CombatPipelineDiagnostics
{
    public const int MaxUnparsedSamples = 8;

    public const int MaxSampleBodyChars = 160;

    public const int MaxObservedSourceChannels = 16;

    public const int MaxSourceChannelChars = 64;

    public required long ClassifiedLineCount { get; init; }

    public required long CanonicalParsedCount { get; init; }

    /// <summary>Legacy <see cref="CombatEventParser.IsCombatShapedUnparsed"/> count. Pet-prefixed bodies are not combat-shaped on the unstripped line.</summary>
    public required long LegacyCombatShapedUnparsedCount { get; init; }

    /// <summary>Canonical-candidate lines with no <c>TryParseCanonical</c> result.</summary>
    public required long CanonicalUnparsedCount { get; init; }

    public required DedupDiagnostics Dedup { get; init; }

    public required AttributionModeHistogram Attribution { get; init; }

    public bool CoverageLimited { get; init; }

    public bool TargetOverflow { get; init; }

    public bool MissingOutgoingDamageType { get; init; }

    public bool MissingIncomingDamageType { get; init; }

    public bool TargetLowerBound { get; init; }

    public bool PetNameRollup { get; init; }

    public LosslessReplayCoverageMatrix? Replay { get; init; }

    public IReadOnlyList<string> ObservedSourceChannels { get; init; } = [];

    public IReadOnlyList<SanitizedUnparsedSample> CanonicalUnparsedSamples { get; init; } = [];

    public required int AnalyticsSemanticVersion { get; init; }

    public required int GrammarSetVersion { get; init; }

    public required int DedupPolicyVersion { get; init; }

    public required int AttributionPolicyVersion { get; init; }

    public required int SegmentSchemaVersion { get; init; }

    public required int SpineSchemaVersion { get; init; }

    public double CanonicalUnparsedRate =>
        ClassifiedLineCount == 0 ? 0 : (double)CanonicalUnparsedCount / ClassifiedLineCount;
}

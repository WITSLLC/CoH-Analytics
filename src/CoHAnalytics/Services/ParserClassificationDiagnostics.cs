namespace CoHAnalytics.Services;

/// <summary>Bounded classification diagnostics containing counts and rule IDs, never event text.</summary>
public sealed record ParserClassificationDiagnostics
{
    public required long SnapshotRevision { get; init; }
    public required long TotalClassifiedLines { get; init; }
    public required long RecognizedLineCount { get; init; }
    public required long UnknownLineCount { get; init; }
    public required long MalformedLineCount { get; init; }
    public required long PotentialIdentityEvidenceCount { get; init; }
    public required long ClassifierFailureCount { get; init; }
    public required DateTimeOffset? LastClassifiedEventAt { get; init; }
    public required IReadOnlyDictionary<string, long> RuleMatchCounts { get; init; }
    public required IReadOnlyList<string> RecentClassifierFailures { get; init; }

    public long UnsupportedPatternCount => UnknownLineCount;

    public double UnknownRate => TotalClassifiedLines == 0
        ? 0
        : (double)UnknownLineCount / TotalClassifiedLines;

    public double MalformedRate => TotalClassifiedLines == 0
        ? 0
        : (double)MalformedLineCount / TotalClassifiedLines;
}

namespace CoHAnalytics.Models;

/// <summary>Immutable aggregate structural-classification counts. Contains no raw lines or names.</summary>
public sealed record ParserClassificationSnapshot
{
    public long TotalClassifiedLines { get; init; }
    public long RecognizedLineCount { get; init; }
    public long UnknownLineCount { get; init; }
    public long MalformedLineCount { get; init; }
    public long PotentialIdentityEvidenceCount { get; init; }
    public long TooLargeLineCount { get; init; }
    public long ClassifierFailureCount { get; init; }
    public DateTimeOffset? LastClassifiedEventAt { get; init; }
    public DateTimeOffset? LastCompleteEventAt { get; init; }
    public DateTimeOffset ObservedAt { get; init; }
    public long Revision { get; init; }

    public static ParserClassificationSnapshot Empty { get; } = new();

    public ParserClassificationSnapshot Apply(IReadOnlyList<ParserEvent> events, DateTimeOffset observedAt)
    {
        if (events.Count == 0)
        {
            return this;
        }

        return this with
        {
            TotalClassifiedLines = TotalClassifiedLines + events.Count,
            RecognizedLineCount = RecognizedLineCount + events.Count(item => item.ClassificationStatus == ParserClassificationStatus.Recognized),
            UnknownLineCount = UnknownLineCount + events.Count(item => item.ClassificationStatus == ParserClassificationStatus.Unknown),
            MalformedLineCount = MalformedLineCount + events.Count(item => item.ClassificationStatus is ParserClassificationStatus.Malformed or ParserClassificationStatus.ClassifierFailed),
            PotentialIdentityEvidenceCount = PotentialIdentityEvidenceCount + events.Count(item => item.StructuralEvidence is not null),
            TooLargeLineCount = TooLargeLineCount + events.Count(item => item.LineStatus == ParserLineStatus.TooLarge),
            ClassifierFailureCount = ClassifierFailureCount + events.Count(item => item.ClassificationStatus == ParserClassificationStatus.ClassifierFailed),
            LastClassifiedEventAt = events.Max(item => item.ObservedAt),
            LastCompleteEventAt = events
                .Where(item => item.LineStatus == ParserLineStatus.Complete)
                .Select(item => (DateTimeOffset?)item.ObservedAt)
                .Max() ?? LastCompleteEventAt,
            ObservedAt = observedAt,
            Revision = Revision + 1
        };
    }
}

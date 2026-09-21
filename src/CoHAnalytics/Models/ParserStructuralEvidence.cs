namespace CoHAnalytics.Models;

public enum ParserStructuralEvidenceKind
{
    WelcomeAttribution,
    SystemAttributedAction
}

public enum ParserAttributionStrength
{
    Strong
}

/// <summary>
/// Structural name attribution only. It performs no account lookup, character match, confidence
/// decision, identity resolution, or gameplay-session mutation.
/// </summary>
public sealed record ParserStructuralEvidence
{
    public required string CandidateName { get; init; }

    public required int EvidenceTextStart { get; init; }

    public required int EvidenceTextLength { get; init; }

    public required ParserStructuralEvidenceKind EvidenceKind { get; init; }

    public required ParserAttributionStrength AttributionStrength { get; init; }
}

namespace CoHAnalytics.Models;

/// <summary>Summary of structural identity evidence observed for the current session.</summary>
public sealed record CharacterIdentityEvidence
{
    public required ParserStructuralEvidenceKind EvidenceKind { get; init; }

    public required string CandidateName { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public required long ParserSequence { get; init; }
}

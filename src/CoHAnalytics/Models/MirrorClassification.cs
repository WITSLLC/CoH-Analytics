namespace CoHAnalytics.Models;

/// <summary>
/// Passive mirror-candidacy metadata. Not a merge trigger, semantic hash, or allowlist decision.
/// </summary>
public sealed record MirrorClassification
{
    public required CombatEventFamily Family { get; init; }

    public required CombatGrammarId GrammarId { get; init; }

    /// <summary>Actual source-channel discriminator when the log supplied one; otherwise null.</summary>
    public string? SourceChannel { get; init; }
}

namespace CoHAnalytics.Models;

/// <summary>
/// Versions CombatEngine metric definitions and projection shape. Independent of
/// <see cref="EventProvenance.CurrentGrammarSetVersion"/> and <see cref="DedupPolicyVersion"/>.
/// </summary>
public static class AnalyticsSemanticVersion
{
    /// <summary>Slice 7 metric availability, evidence, and observed-time contract.</summary>
    public const int Current = 2;
}

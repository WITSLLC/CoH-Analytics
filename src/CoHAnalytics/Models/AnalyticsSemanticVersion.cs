namespace CoHAnalytics.Models;

/// <summary>
/// Versions CombatEngine metric definitions and projection shape. Independent of
/// <see cref="EventProvenance.CurrentGrammarSetVersion"/> and <see cref="DedupPolicyVersion"/>.
/// </summary>
public static class AnalyticsSemanticVersion
{
    /// <summary>Slice 6 dimensioned accumulators and <c>CombatAnalyticsProjection</c>.</summary>
    public const int Current = 1;
}

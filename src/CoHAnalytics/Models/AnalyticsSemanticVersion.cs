namespace CoHAnalytics.Models;

/// <summary>
/// Versions CombatEngine metric definitions and projection shape. Independent of
/// <see cref="EventProvenance.CurrentGrammarSetVersion"/> and <see cref="DedupPolicyVersion"/>.
/// </summary>
public static class AnalyticsSemanticVersion
{
    /// <summary>Slice 8 frozen build context and four-mode proc attribution.</summary>
    public const int Current = 3;
}

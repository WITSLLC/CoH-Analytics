namespace CoHAnalytics.Models;

/// <summary>
/// Versions the mirror allowlist, required independent discriminator, candidate band,
/// counterpart rules, survivor selection, and facet union. Not a persistence schema.
/// </summary>
public static class DedupPolicyVersion
{
    /// <summary>v1 is an empty allowlist. Families are added only when fixtures prove them.</summary>
    public const int Current = 1;
}

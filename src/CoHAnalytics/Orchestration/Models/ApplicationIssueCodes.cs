namespace CoHAnalytics.Orchestration.Models;

/// <summary>
/// Stable codes for the issues the orchestrator itself synthesizes. Contributor-authored
/// issue codes are namespaced by the contributor and do not belong here.
/// </summary>
public static class ApplicationIssueCodes
{
    /// <summary>A required capability has no producer, or its producer reported failure.</summary>
    public const string DependencyUnresolved = "orchestrator.dependency_unresolved";

    /// <summary>A required capability resolved, but its producer is degraded or stale.</summary>
    public const string DependencyDegraded = "orchestrator.dependency_degraded";

    /// <summary>
    /// A required capability has a registered producer that has not been observed yet (§9.2.1).
    /// Informational and never actionable: nothing is known to be wrong.
    /// </summary>
    public const string DependencyPending = "orchestrator.dependency_pending";

    /// <summary>A contributor's lifecycle start or stop failed.</summary>
    public const string ContributorLifecycleFailed = "orchestrator.contributor_lifecycle_failed";

    /// <summary>A contributor's contribution is stale beyond its declared freshness limit.</summary>
    public const string ContributorStale = "orchestrator.contributor_stale";

    /// <summary>A contributor threw while producing a contribution.</summary>
    public const string ContributorFailed = "orchestrator.contributor_failed";

    /// <summary>A contributor pull exceeded its timeout with no prior contribution to retain.</summary>
    public const string ContributorTimeout = "orchestrator.contributor_timeout";
}

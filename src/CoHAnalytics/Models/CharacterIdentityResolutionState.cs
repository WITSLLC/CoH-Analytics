namespace CoHAnalytics.Models;

/// <summary>
/// Domain-only identity resolution workflow state for a gameplay session (Revision 9
/// §3.6.22.2). Independent from <see cref="CharacterIdentityConfidence"/> and never added to
/// orchestrator-normalized enums.
/// </summary>
public enum CharacterIdentityResolutionState
{
    /// <summary>No usable character candidate is available yet.</summary>
    Unresolved,

    /// <summary>
    /// One or more first-use candidates exist, but none may be assigned automatically.
    /// </summary>
    Candidate,

    /// <summary>Exactly one <see cref="CharacterRecord"/> is assigned to the current session.</summary>
    Resolved,

    /// <summary>
    /// Evidence or repository state cannot be reconciled to one safe account-scoped character.
    /// </summary>
    Conflicted,

    /// <summary>
    /// Unresolved retention reached its bound; user confirmation is required while parser
    /// delivery continues.
    /// </summary>
    IdentityRequired
}

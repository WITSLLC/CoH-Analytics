namespace CoHAnalytics.Models;

/// <summary>
/// Domain-only identity confidence for a gameplay session (Revision 9 §3.6.22.2). Never added to
/// <c>ContributorHealth</c>, <c>ContributorActivity</c>, or any orchestrator-normalized enum.
/// </summary>
public enum CharacterIdentityConfidence
{
    /// <summary>No character identity has been established for the current gameplay session.</summary>
    Unknown,

    /// <summary>
    /// A strong exact account-scoped attributed match resolved to one previously trusted
    /// character record.
    /// </summary>
    Inferred,

    /// <summary>A Welcome message or explicit manual selection established the character.</summary>
    Confirmed
}

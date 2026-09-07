namespace CoHAnalytics.Models;

/// <summary>
/// How a character record became trusted in the Character Repository (Revision 9 §3.6.22.5).
/// Only Welcome evidence and explicit manual confirmation may establish trust.
/// </summary>
public enum CharacterTrustState
{
    /// <summary>Established from a Welcome message observed in monitored chat.</summary>
    TrustedFromWelcome,

    /// <summary>Established from an explicit user manual character selection.</summary>
    TrustedFromManualConfirmation
}

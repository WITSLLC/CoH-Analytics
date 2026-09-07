namespace CoHAnalytics.Models;

/// <summary>Runtime lifecycle for one gameplay session (Revision 9 §3.6.22).</summary>
public enum GameplaySessionLifecycleState
{
    /// <summary>The session is actively receiving parser events.</summary>
    Active,

    /// <summary>
    /// Processing is paused while the monitoring context is runtime-suspended or the source is
    /// temporarily unavailable. Identity and retained events are preserved.
    /// </summary>
    Suspended,

    /// <summary>The session ended and will not receive further events.</summary>
    Finalized
}

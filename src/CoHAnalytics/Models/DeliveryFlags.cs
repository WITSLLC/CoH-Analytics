namespace CoHAnalytics.Models;

/// <summary>Existing delivery/status flags ported from today's parser semantics.</summary>
[Flags]
public enum DeliveryFlags
{
    None = 0,
    DoT = 1,
    Containment = 2,
    Overpower = 4,
    Autohit = 8,
    Forced = 16,
    Critical = 32
}

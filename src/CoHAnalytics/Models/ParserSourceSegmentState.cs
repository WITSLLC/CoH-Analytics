namespace CoHAnalytics.Models;

/// <summary>Parser-side lifecycle state for a source segment.</summary>
public enum ParserSourceSegmentState
{
    Active,
    Completed,
    RolledOver,
    TruncationReset,
    Replaced,
    Released,
    Removed,
    Faulted
}

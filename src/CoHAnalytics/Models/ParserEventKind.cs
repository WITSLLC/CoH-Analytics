namespace CoHAnalytics.Models;

/// <summary>Conservative structural category for one complete parser line.</summary>
public enum ParserEventKind
{
    Unknown,
    TimestampedLine,
    SystemLine,
    ChatLine,
    PotentialIdentityEvidence,
    Malformed
}

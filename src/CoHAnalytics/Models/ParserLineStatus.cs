namespace CoHAnalytics.Models;

/// <summary>Minimal Slice 6B disposition of one framed source line.</summary>
public enum ParserLineStatus
{
    /// <summary>A complete strictly decoded UTF-8 line.</summary>
    Complete,

    /// <summary>Reserved for an explicit malformed-line envelope; Slice 6B faults by default.</summary>
    MalformedEncoding,

    /// <summary>The framed line exceeded the configured bounded raw-line buffer.</summary>
    TooLarge
}

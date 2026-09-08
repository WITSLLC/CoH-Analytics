namespace CoHAnalytics.Services;

/// <summary>Minimal clipboard seam for copy-to-clipboard UI actions.</summary>
public interface IClipboardService
{
    /// <summary>
    /// Attempts to place <paramref name="text"/> on the clipboard.
    /// Returns <c>false</c> for null/whitespace text or when clipboard open fails after retries.
    /// </summary>
    bool TrySetText(string? text);
}

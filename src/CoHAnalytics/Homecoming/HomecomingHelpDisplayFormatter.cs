using System.Text.RegularExpressions;

namespace CoHAnalytics.Homecoming;

/// <summary>
/// Converts a small subset of Homecoming help markup into plain text suitable for WPF tooltips.
/// Canonical reference/help strings are not modified at the source; call this at UI presentation time.
/// </summary>
public static class HomecomingHelpDisplayFormatter
{
    private static readonly Regex BrTagRegex = new(
        @"<br\s*/?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ColorOpenTagRegex = new(
        @"<color\s[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ColorCloseTagRegex = new(
        @"</color\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? NormalizeForDisplay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (!ContainsSupportedMarkup(text))
        {
            return text;
        }

        var normalized = BrTagRegex.Replace(text, Environment.NewLine);
        normalized = ColorOpenTagRegex.Replace(normalized, string.Empty);
        normalized = ColorCloseTagRegex.Replace(normalized, string.Empty);
        normalized = CollapseExcessiveBlankLines(normalized);
        return normalized.Trim();
    }

    private static bool ContainsSupportedMarkup(string text) =>
        text.Contains("<br", StringComparison.OrdinalIgnoreCase)
        || text.Contains("<color", StringComparison.OrdinalIgnoreCase)
        || text.Contains("</color", StringComparison.OrdinalIgnoreCase);

    private static string CollapseExcessiveBlankLines(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var result = new List<string>(lines.Length);
        var consecutiveBlankLines = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                consecutiveBlankLines++;
                if (consecutiveBlankLines <= 1)
                {
                    result.Add(string.Empty);
                }

                continue;
            }

            consecutiveBlankLines = 0;
            result.Add(line.TrimEnd());
        }

        return string.Join(Environment.NewLine, result);
    }
}

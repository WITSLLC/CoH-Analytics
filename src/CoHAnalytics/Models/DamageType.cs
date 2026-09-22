using System.Globalization;

namespace CoHAnalytics.Models;

/// <summary>
/// Canonical damage-type value object. Preserves parsed textual identity, including multiword
/// types, and records unresistable/unique flags only when those words appear in the capture.
/// </summary>
public readonly record struct DamageType
{
    public DamageType(string text, bool isUnresistable = false, bool isUnique = false)
    {
        Text = text;
        IsUnresistable = isUnresistable;
        IsUnique = isUnique;
    }

    public string Text { get; }

    public bool IsUnresistable { get; }

    public bool IsUnique { get; }

    public static DamageType FromParsedToken(string token)
    {
        var trimmed = token.Trim();
        var isUnresistable = trimmed.StartsWith("unresistable ", StringComparison.OrdinalIgnoreCase);
        var remainder = isUnresistable ? trimmed["unresistable ".Length..].Trim() : trimmed;
        if (remainder.Length == 0)
        {
            remainder = trimmed;
            isUnresistable = false;
        }

        var isUnique = remainder.Equals("Unique", StringComparison.OrdinalIgnoreCase);
        var text = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(remainder.ToLowerInvariant());
        return new DamageType(text, isUnresistable, isUnique);
    }
}

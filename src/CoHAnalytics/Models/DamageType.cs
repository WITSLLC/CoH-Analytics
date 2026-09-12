using System.Globalization;

namespace CoHAnalytics.Models;

/// <summary>
/// Canonical damage-type value object. Slice 2 stores the currently parsed single-token text
/// using existing title-case conversion; multiword and unresistable parsing belong to Slice 3.
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

    public static DamageType FromParsedToken(string token) =>
        new(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(token.ToLowerInvariant()));
}

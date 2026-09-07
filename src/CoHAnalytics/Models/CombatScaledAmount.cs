using System.Globalization;

namespace CoHAnalytics.Models;

/// <summary>
/// Exact combat magnitude stored in hundredths to avoid floating-point accumulation drift.
/// A log value of <c>13.88</c> is stored as <see cref="Hundredths"/> <c>1388</c>.
/// </summary>
public readonly struct CombatScaledAmount : IEquatable<CombatScaledAmount>
{
    public const int Scale = 100;

    public CombatScaledAmount(long hundredths)
    {
        Hundredths = hundredths;
    }

    public long Hundredths { get; }

    public static CombatScaledAmount Zero { get; } = new(0);

    public static bool TryParse(string text, out CombatScaledAmount amount)
    {
        amount = Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('.');
        if (parts.Length is < 1 or > 2)
        {
            return false;
        }

        if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var whole)
            || whole < 0)
        {
            return false;
        }

        long fraction = 0;
        if (parts.Length == 2)
        {
            var fractionText = parts[1];
            if (fractionText.Length is 0 or > 2
                || !long.TryParse(fractionText, NumberStyles.None, CultureInfo.InvariantCulture, out fraction)
                || fraction < 0)
            {
                return false;
            }

            if (fractionText.Length == 1)
            {
                fraction *= 10;
            }
        }

        amount = new CombatScaledAmount(whole * Scale + fraction);
        return true;
    }

    public bool Equals(CombatScaledAmount other) => Hundredths == other.Hundredths;

    public override bool Equals(object? obj) => obj is CombatScaledAmount other && Equals(other);

    public override int GetHashCode() => Hundredths.GetHashCode();

    public static bool operator ==(CombatScaledAmount left, CombatScaledAmount right) => left.Equals(right);

    public static bool operator !=(CombatScaledAmount left, CombatScaledAmount right) => !left.Equals(right);

    public static CombatScaledAmount operator +(CombatScaledAmount left, CombatScaledAmount right) =>
        new(left.Hundredths + right.Hundredths);

    public override string ToString() =>
        (Hundredths / (decimal)Scale).ToString("0.##", CultureInfo.InvariantCulture);
}

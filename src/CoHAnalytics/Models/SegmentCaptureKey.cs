using System.Globalization;

namespace CoHAnalytics.Models;

/// <summary>
/// Durable capture identity <c>{GameplaySessionId}_{ordinal:D10}</c>. The ordinal is part of the
/// on-disk key so a future multi-segment capture cannot reinterpret Slice 9 session-scoped IDs.
/// </summary>
public static class SegmentCaptureKey
{
    public const int OrdinalWidth = 10;

    public static string Format(GameplaySessionId gameplaySessionId, int segmentOrdinal)
    {
        ArgumentNullException.ThrowIfNull(gameplaySessionId);
        if (segmentOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentOrdinal), "Segment ordinal must be non-negative.");
        }

        return $"{gameplaySessionId}_{segmentOrdinal.ToString($"D{OrdinalWidth}", CultureInfo.InvariantCulture)}";
    }

    public static bool TryParse(string? value, out GameplaySessionId gameplaySessionId, out int segmentOrdinal)
    {
        gameplaySessionId = null!;
        segmentOrdinal = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separator = value.IndexOf('_');
        if (separator <= 0 || separator != value.LastIndexOf('_'))
        {
            return false;
        }

        var left = value[..separator];
        var right = value[(separator + 1)..];
        if (right.Length != OrdinalWidth
            || !Guid.TryParseExact(left, "n", out var guid)
            || !int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out segmentOrdinal)
            || segmentOrdinal < 0)
        {
            return false;
        }

        gameplaySessionId = GameplaySessionId.FromGuid(guid);
        return true;
    }
}

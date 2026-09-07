using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Resolves combat-event timestamps for rolling-window bucket placement.
/// Session and tracked totals continue to use the full event stream.
/// </summary>
internal static class RollingCombatTimestamp
{
    internal static bool TryResolveBucketSecond(CombatEvent combatEvent, out long unixSecond)
    {
        unixSecond = 0;

        if (combatEvent.SourceTimestamp is { } sourceTimestamp)
        {
            if (!HasSecondPrecision(sourceTimestamp))
            {
                return false;
            }

            unixSecond = ToUnixSecond(sourceTimestamp, combatEvent.ObservedAt);
            return true;
        }

        unixSecond = combatEvent.ObservedAt.ToUnixTimeSeconds();
        return true;
    }

    internal static bool HasSecondPrecision(DateTime sourceTimestamp) =>
        sourceTimestamp is { Hour: 0, Minute: 0, Second: 0, Millisecond: 0 }
            ? false
            : true;

    internal static long ToUnixSecond(DateTime sourceTimestamp, DateTimeOffset observedAt)
    {
        var normalized = sourceTimestamp.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(DateTime.SpecifyKind(sourceTimestamp, DateTimeKind.Utc)),
            DateTimeKind.Local => new DateTimeOffset(sourceTimestamp),
            // Homecoming log timestamps are wall-clock on the player's machine.
            _ => new DateTimeOffset(sourceTimestamp, TimeZoneInfo.Local.GetUtcOffset(sourceTimestamp))
        };

        return normalized.ToUnixTimeSeconds();
    }
}

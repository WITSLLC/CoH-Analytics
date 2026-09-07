using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Resolves gameplay telemetry timestamps for rolling earnings bucket placement.
/// Session and tracked totals continue to use the full committed event stream.
/// </summary>
internal static class RollingEarningsTimestamp
{
    internal static bool TryResolveBucketSecond(ParserEvent parserEvent, out long unixSecond)
    {
        unixSecond = 0;

        if (parserEvent.SourceTimestamp is { } sourceTimestamp)
        {
            if (!RollingCombatTimestamp.HasSecondPrecision(sourceTimestamp))
            {
                return false;
            }

            unixSecond = RollingCombatTimestamp.ToUnixSecond(sourceTimestamp, parserEvent.ObservedAt);
            return true;
        }

        unixSecond = parserEvent.ObservedAt.ToUnixTimeSeconds();
        return true;
    }
}

namespace CoHAnalytics.Models;

/// <summary>
/// Immutable rolling earnings projections for all supported preset widths.
/// Preset selection is a presentation concern; domain retains the full horizon.
/// </summary>
public sealed record RollingEarningsScopeSnapshot
{
    public static RollingEarningsScopeSnapshot Empty { get; } = new();

    public bool TimestampPrecisionUnavailable { get; init; }

    public RollingEarningsWindowSnapshot OneMinute { get; init; } = RollingEarningsWindowSnapshot.Empty;

    public RollingEarningsWindowSnapshot TwoMinutes { get; init; } = RollingEarningsWindowSnapshot.Empty;

    public RollingEarningsWindowSnapshot FiveMinutes { get; init; } = RollingEarningsWindowSnapshot.Empty;

    public RollingEarningsWindowSnapshot TenMinutes { get; init; } = RollingEarningsWindowSnapshot.Empty;

    public RollingEarningsWindowSnapshot FifteenMinutes { get; init; } = RollingEarningsWindowSnapshot.Empty;

    public RollingEarningsWindowSnapshot GetPreset(int windowMinutes) => windowMinutes switch
    {
        RollingCombatPresets.OneMinute => OneMinute,
        RollingCombatPresets.TwoMinutes => TwoMinutes,
        RollingCombatPresets.FiveMinutes => FiveMinutes,
        RollingCombatPresets.TenMinutes => TenMinutes,
        RollingCombatPresets.FifteenMinutes => FifteenMinutes,
        _ => TenMinutes
    };
}

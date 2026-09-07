namespace CoHAnalytics.Models;

/// <summary>
/// Immutable rolling combat projections for all supported preset widths.
/// Preset selection is a presentation concern; domain retains the full horizon.
/// </summary>
public sealed record RollingCombatScopeSnapshot
{
    public static RollingCombatScopeSnapshot Empty { get; } = new();

    public bool TimestampPrecisionUnavailable { get; init; }

    public RollingCombatWindowSnapshot OneMinute { get; init; } = RollingCombatWindowSnapshot.Empty;

    public RollingCombatWindowSnapshot TwoMinutes { get; init; } = RollingCombatWindowSnapshot.Empty;

    public RollingCombatWindowSnapshot FiveMinutes { get; init; } = RollingCombatWindowSnapshot.Empty;

    public RollingCombatWindowSnapshot TenMinutes { get; init; } = RollingCombatWindowSnapshot.Empty;

    public RollingCombatWindowSnapshot FifteenMinutes { get; init; } = RollingCombatWindowSnapshot.Empty;

    public RollingCombatWindowSnapshot GetPreset(int windowMinutes) => windowMinutes switch
    {
        RollingCombatPresets.OneMinute => OneMinute,
        RollingCombatPresets.TwoMinutes => TwoMinutes,
        RollingCombatPresets.FiveMinutes => FiveMinutes,
        RollingCombatPresets.TenMinutes => TenMinutes,
        RollingCombatPresets.FifteenMinutes => FifteenMinutes,
        _ => TenMinutes
    };
}

/// <summary>Supported rolling DPS preset widths in minutes.</summary>
public static class RollingCombatPresets
{
    public const int OneMinute = 1;
    public const int TwoMinutes = 2;
    public const int FiveMinutes = 5;
    public const int TenMinutes = 10;
    public const int FifteenMinutes = 15;

    public const int DefaultMinutes = TenMinutes;

    public const int MaximumMinutes = FifteenMinutes;

    public const int MaximumHorizonSeconds = MaximumMinutes * 60;

    public static readonly int[] SupportedMinutes =
    [
        OneMinute,
        TwoMinutes,
        FiveMinutes,
        TenMinutes,
        FifteenMinutes
    ];
}

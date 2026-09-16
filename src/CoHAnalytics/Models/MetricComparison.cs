namespace CoHAnalytics.Models;

/// <summary>Per-field comparison outcome from frozen §21.</summary>
public enum ComparisonState
{
    Comparable = 0,
    Incompatible = 1,
    Unavailable = 2,
    Partial = 3
}

/// <summary>Typed reason for a non-comparable or incomplete comparison. Prefer this over free-form text.</summary>
public enum ComparisonReason
{
    None = 0,
    ZeroBaseline = 1,
    NotCaptured = 2,
    Unsupported = 3,
    Incomplete = 4,
    DenominatorMismatch = 5,
    SemanticVersionMismatch = 6,
    AttributionPolicyMismatch = 7,
    CoverageLimited = 8,
    TargetLowerBound = 9,
    MissingDamageType = 10,
    Overflow = 11,
    LeftOnly = 12,
    RightOnly = 13,
    ArithmeticOverflow = 14,
    DuplicateKey = 15
}

/// <summary>Whole-comparison compatibility. Distinct from per-field <see cref="ComparisonState"/>.</summary>
public enum AnalyticalComparisonCompatibility
{
    Comparable = 0,
    PartiallyComparable = 1,
    IncompatibleSemanticVersion = 2,
    IncompatibleMetricSemantics = 3,
    InsufficientData = 4
}

public enum ComparisonPresence
{
    Matched = 0,
    LeftOnly = 1,
    RightOnly = 2,
    Ambiguous = 3
}

/// <summary>
/// Typed metric pair. Absolute delta is Right − Left. Percent is (Right − Left) / |Left|
/// stored as hundredths of a percent (10_000 = 100%). Percentage points use the same
/// hundredths scale (500 = 5.00 pp). A zero left baseline never yields ∞.
/// </summary>
public readonly record struct MetricComparison<T> where T : struct
{
    public Metric<T> Left { get; init; }

    public Metric<T> Right { get; init; }

    public ComparisonState State { get; init; }

    public ComparisonReason Reason { get; init; }

    public Metric<T> AbsoluteDelta { get; init; }

    /// <summary>Relative change in hundredths of a percent. Not a percentage-point delta.</summary>
    public Metric<long> PercentDeltaHundredths { get; init; }

    /// <summary>
    /// Why the derived percent is absent. This is separate from <see cref="Reason"/> so a
    /// valid absolute comparison with a zero baseline is still a comparable metric pair.
    /// </summary>
    public ComparisonReason PercentDeltaReason { get; init; }

    /// <summary>Percentage-point delta in hundredths (500 = +5.00 pp). Unused except for rates-as-%.</summary>
    public Metric<long> PercentagePointDeltaHundredths { get; init; }

    public static MetricComparison<T> Unavailable(Metric<T> left, Metric<T> right, ComparisonReason reason) =>
        new()
        {
            Left = left,
            Right = right,
            State = ComparisonState.Unavailable,
            Reason = reason,
            AbsoluteDelta = Metric<T>.NotCaptured(),
            PercentDeltaHundredths = Metric<long>.NotCaptured(),
            PercentDeltaReason = reason,
            PercentagePointDeltaHundredths = Metric<long>.NotCaptured()
        };

    public static MetricComparison<T> Incompatible(Metric<T> left, Metric<T> right, ComparisonReason reason) =>
        new()
        {
            Left = left,
            Right = right,
            State = ComparisonState.Incompatible,
            Reason = reason,
            AbsoluteDelta = Metric<T>.NotCaptured(),
            PercentDeltaHundredths = Metric<long>.NotCaptured(),
            PercentDeltaReason = reason,
            PercentagePointDeltaHundredths = Metric<long>.NotCaptured()
        };
}

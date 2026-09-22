namespace CoHAnalytics.Models;

/// <summary>
/// Whether a projected value may be treated as a complete observation.
/// Default <see cref="NotCaptured"/> is the safe unset state; it is never an observed zero.
/// </summary>
public enum MetricAvailability
{
    NotCaptured = 0,
    Available = 1,
    Incomplete = 2,
    Unsupported = 3
}

/// <summary>
/// Inferred-quality band from the frozen architecture. Used only when a value is inferred,
/// never as a substitute for availability.
/// </summary>
public enum MetricConfidence
{
    High = 1,
    Medium = 2,
    Low = 3
}

/// <summary>Evidence class for a projected metric. None is the unset/default state.</summary>
public enum MetricEvidence
{
    None = 0,
    DirectObserved = 1,
    DerivedFromObserved = 2,
    CoverageLimited = 3
}

/// <summary>
/// Frozen section 14 denominator vocabulary, not a capability list. Slice 7 projects capture-wall
/// rates and tracked duration; Active remains unsupported and RollingWindow is legacy-only.
/// </summary>
public enum RateDenominatorKind
{
    Unspecified = 0,
    WallClock = 1,
    TrackedPauseAdjusted = 2,
    Active = 3,
    RollingWindow = 4
}

/// <summary>Optional coverage limitation attached to a metric. Not a second availability enum.</summary>
public readonly record struct CoverageInfo
{
    public bool Overflow { get; init; }

    public bool LowerBound { get; init; }

    /// <summary>Name-based identity only; does not imply loss of captured magnitude.</summary>
    public bool PetNameRollup { get; init; }

    public bool MissingDamageType { get; init; }

    public bool MissingTarget { get; init; }

    public bool MissingBuildContext { get; init; }

    public bool AmbiguousProcParent { get; init; }

    public bool UnidentifiedProcSource { get; init; }

    internal bool IsPartial => Overflow || LowerBound || MissingDamageType || MissingTarget
        || MissingBuildContext || AmbiguousProcParent || UnidentifiedProcSource;
}

/// <summary>
/// Typed analytical value for struct metrics. <see cref="Value"/> is omitted (null) when
/// availability is <see cref="MetricAvailability.NotCaptured"/> or
/// <see cref="MetricAvailability.Unsupported"/>.
/// </summary>
public readonly record struct Metric<T> where T : struct
{
    public T? Value { get; private init; }

    public MetricAvailability Availability { get; private init; }

    public MetricConfidence? Confidence { get; private init; }

    public MetricEvidence Evidence { get; private init; }

    public RateDenominatorKind? Denominator { get; private init; }

    public CoverageInfo? Coverage { get; private init; }

    public bool HasCompleteValue =>
        Availability == MetricAvailability.Available && Value.HasValue;

    public static Metric<T> Available(
        T value,
        MetricEvidence evidence = MetricEvidence.DirectObserved,
        MetricConfidence? confidence = null,
        RateDenominatorKind? denominator = null,
        CoverageInfo? coverage = null)
    {
        MetricValidation.Validate(MetricAvailability.Available, evidence, confidence, coverage, denominator);
        return new()
        {
            Value = value,
            Availability = MetricAvailability.Available,
            Evidence = evidence,
            Confidence = confidence,
            Denominator = denominator,
            Coverage = coverage
        };
    }

    public static Metric<T> Incomplete(
        T value,
        MetricEvidence evidence = MetricEvidence.CoverageLimited,
        CoverageInfo? coverage = null,
        MetricConfidence? confidence = null)
    {
        MetricValidation.Validate(MetricAvailability.Incomplete, evidence, confidence, coverage);
        return new()
        {
            Value = value,
            Availability = MetricAvailability.Incomplete,
            Evidence = evidence,
            Confidence = confidence,
            Coverage = coverage
        };
    }

    public static Metric<T> NotCaptured() =>
        new() { Availability = MetricAvailability.NotCaptured };

    public static Metric<T> Unsupported() =>
        new() { Availability = MetricAvailability.Unsupported };
}

/// <summary>Typed analytical value for reference-type snapshots such as accuracy.</summary>
public sealed record MetricRef<T> where T : class
{
    public T? Value { get; private init; }

    public MetricAvailability Availability { get; private init; }

    public MetricConfidence? Confidence { get; private init; }

    public MetricEvidence Evidence { get; private init; }

    public CoverageInfo? Coverage { get; private init; }

    public static MetricRef<T> Available(
        T value,
        MetricEvidence evidence = MetricEvidence.DirectObserved,
        MetricConfidence? confidence = null,
        CoverageInfo? coverage = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        MetricValidation.Validate(MetricAvailability.Available, evidence, confidence, coverage);
        return new()
        {
            Value = value,
            Availability = MetricAvailability.Available,
            Evidence = evidence,
            Confidence = confidence,
            Coverage = coverage
        };
    }

    public static MetricRef<T> Incomplete(T value, CoverageInfo coverage)
    {
        ArgumentNullException.ThrowIfNull(value);
        MetricValidation.Validate(MetricAvailability.Incomplete, MetricEvidence.CoverageLimited, null, coverage);
        return new() { Value = value, Availability = MetricAvailability.Incomplete,
            Evidence = MetricEvidence.CoverageLimited, Coverage = coverage };
    }

    public static MetricRef<T> NotCaptured() =>
        new() { Availability = MetricAvailability.NotCaptured };

    public static MetricRef<T> Unsupported() =>
        new() { Availability = MetricAvailability.Unsupported };
}

internal static class MetricValidation
{
    internal static void Validate(MetricAvailability availability, MetricEvidence evidence,
        MetricConfidence? confidence, CoverageInfo? coverage, RateDenominatorKind? denominator = null)
    {
        if (availability == MetricAvailability.Available
            ? evidence is not (MetricEvidence.DirectObserved or MetricEvidence.DerivedFromObserved)
            : evidence != MetricEvidence.CoverageLimited)
            throw new ArgumentException("Evidence must agree with availability.", nameof(evidence));
        if (availability == MetricAvailability.Available && coverage is { IsPartial: true })
            throw new ArgumentException("Partial coverage requires Incomplete.", nameof(coverage));
        if (availability == MetricAvailability.Incomplete && coverage is { IsPartial: false })
            throw new ArgumentException("Coverage must identify a limitation.", nameof(coverage));
        if (confidence is { } band && (!Enum.IsDefined(band) || evidence == MetricEvidence.DirectObserved))
            throw new ArgumentException("Confidence is only for inferred values.", nameof(confidence));
        if (denominator is { } kind && (!Enum.IsDefined(kind) || kind == RateDenominatorKind.Unspecified))
            throw new ArgumentException("A supplied denominator must be explicit.", nameof(denominator));
    }
}

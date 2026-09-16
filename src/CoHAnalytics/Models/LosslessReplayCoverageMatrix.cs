namespace CoHAnalytics.Models;

/// <summary>
/// Frozen §17/§18 field-level replay authority. Aggregate authority is independent of spine
/// replay: a truncated spine never silently replaces cube totals.
/// </summary>
/// <remarks>
/// "Lossless" is scoped to the normalized logical event spine under the pinned semantic and
/// policy versions persisted with the Segment. It does NOT imply raw-log recoverability,
/// physical duplicate-occurrence recoverability, original chat/system text recoverability, or
/// the ability to re-run parsing/dedup from raw input. It means only that re-accumulating the
/// retained post-parse, post-dedup logical events under the pinned <c>AnalyticsSemanticVersion</c>
/// reproduces the same supported analytical dimension exactly.
/// </remarks>
public enum ReplayCoverageKind
{
    Lossless = 0,
    SufficientForRecompute = 1,
    PartialReplay = 2,
    NotRecomputable = 3
}

/// <summary>One matrix cell: whether the cube is authoritative and whether the spine can replay it.</summary>
public sealed record ReplayCoverageEntry
{
    public required bool AggregateAuthoritative { get; init; }

    public required ReplayCoverageKind Replay { get; init; }
}

/// <summary>Typed Lossless Replay Coverage Matrix. Not free-form prose.</summary>
public sealed record LosslessReplayCoverageMatrix
{
    public const int CurrentMatrixVersion = 1;

    public int MatrixVersion { get; init; } = CurrentMatrixVersion;

    public ReplayCoverageEntry SessionTotals { get; init; } = CubeOnly();

    public ReplayCoverageEntry PerPowerTotals { get; init; } = CubeOnly();

    public ReplayCoverageEntry DamageType { get; init; } = CubeOnly();

    public ReplayCoverageEntry DirectVersusDot { get; init; } = CubeOnly();

    public ReplayCoverageEntry ActorPet { get; init; } = CubeOnly();

    public ReplayCoverageEntry Target { get; init; } = CubeOnly();

    public ReplayCoverageEntry Accuracy { get; init; } = CubeOnly();

    public ReplayCoverageEntry Activation { get; init; } = CubeOnly();

    public ReplayCoverageEntry LifecycleRecharge { get; init; } = CubeOnly();

    public ReplayCoverageEntry Clock { get; init; } = CubeOnly();

    public ReplayCoverageEntry FrozenBuildContext { get; init; } = CubeOnly();

    public ReplayCoverageEntry ProcAttribution { get; init; } = CubeOnly();

    public ReplayCoverageEntry EventTimeline { get; init; } = new()
    {
        AggregateAuthoritative = false,
        Replay = ReplayCoverageKind.NotRecomputable
    };

    public static LosslessReplayCoverageMatrix ForCapture(
        bool spineTruncated,
        bool allLogicalEventsRetained,
        bool frozenBuildPersisted)
    {
        var cubeReplay = allLogicalEventsRetained
            ? ReplayCoverageKind.Lossless
            : ReplayCoverageKind.NotRecomputable;
        var timeline = !spineTruncated && allLogicalEventsRetained
            ? ReplayCoverageKind.Lossless
            : spineTruncated
                ? ReplayCoverageKind.PartialReplay
                : ReplayCoverageKind.NotRecomputable;
        ReplayCoverageEntry Cube() => new()
        {
            AggregateAuthoritative = true,
            Replay = cubeReplay
        };

        return new LosslessReplayCoverageMatrix
        {
            SessionTotals = Cube(),
            PerPowerTotals = Cube(),
            DamageType = Cube(),
            DirectVersusDot = Cube(),
            ActorPet = Cube(),
            Target = Cube(),
            Accuracy = Cube(),
            Activation = Cube(),
            LifecycleRecharge = Cube(),
            // Clock is cube-authoritative but NOT spine-recomputable: the final clock includes
            // tracked pause-adjusted duration derived from tracked-lifecycle state that is never
            // represented in the spine, and truncation would break first/last observation bounds.
            Clock = new ReplayCoverageEntry
            {
                AggregateAuthoritative = true,
                Replay = ReplayCoverageKind.NotRecomputable
            },
            FrozenBuildContext = new ReplayCoverageEntry
            {
                AggregateAuthoritative = frozenBuildPersisted,
                Replay = ReplayCoverageKind.NotRecomputable
            },
            ProcAttribution = new ReplayCoverageEntry
            {
                AggregateAuthoritative = true,
                Replay = ReplayCoverageKind.NotRecomputable
            },
            EventTimeline = new ReplayCoverageEntry
            {
                AggregateAuthoritative = false,
                Replay = timeline
            }
        };
    }

    private static ReplayCoverageEntry CubeOnly() => new()
    {
        AggregateAuthoritative = true,
        Replay = ReplayCoverageKind.NotRecomputable
    };
}

using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Maps retained <see cref="CharacterPerformanceObservation"/> v1/v2 files into the current
/// <see cref="CombatAnalyticsProjection"/> shape without applying live engine formulas.
/// Missing axes are <see cref="MetricAvailability.NotCaptured"/>, never observed zero.
/// </summary>
public static class LegacyObservationAdapter
{
    public static CombatAnalyticsProjection ToProjection(CharacterPerformanceObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var accuracy = new CombatAccuracyScopeSnapshot
        {
            Attempts = observation.Attempts,
            Hits = observation.Hits,
            Misses = observation.Misses,
            RolledAttempts = observation.RolledAttempts,
            DisplayedChanceSumHundredths = observation.DisplayedChanceSumHundredths,
            RollSumHundredths = observation.RollSumHundredths,
            ForcedHits = observation.ForcedHits,
            Autohits = observation.Autohits
        };
        var duration = observation.ObservedDuration;
        return new CombatAnalyticsProjection
        {
            AnalyticsSemanticVersion = AnalyticsSemanticVersion.Current,
            Session = new CombatSessionSummary
            {
                DamageDealt = observation.DamageDealt,
                DefeatCount = observation.TotalDefeated,
                MyDefeatCount = observation.MyDefeats,
                Accuracy = accuracy,
                CoverageLimited = true,
                Metrics = CombatSessionMetricSet.Empty with
                {
                    DamageDealt = Metric<CombatScaledAmount>.Available(observation.DamageDealt),
                    Accuracy = MetricRef<CombatAccuracyScopeSnapshot>.Available(accuracy)
                }
            },
            Clock = new SegmentClock
            {
                CaptureStartUtc = observation.StartedAtUtc,
                CaptureEndUtc = observation.EndedAtUtc,
                AsOfUtc = observation.EndedAtUtc,
                WallClockDuration = Metric<TimeSpan>.Available(
                    duration,
                    MetricEvidence.DerivedFromObserved,
                    denominator: RateDenominatorKind.WallClock),
                ActiveDuration = Metric<TimeSpan>.Unsupported()
            },
            BuildContext = CombatBuildContextSummary.NotCaptured,
            Attribution = CombatProcAttributionSummary.Empty,
            CoverageLimited = true
        };
    }

    public static SegmentCoverageDescriptor ToCoverage(CharacterPerformanceObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return new SegmentCoverageDescriptor
        {
            LogicalEventCount = 0,
            DuplicateOccurrencesIgnored = 0,
            RetainedSpineEventCount = 0,
            SpineRetentionLimit = SegmentSpineLimits.MaxRetainedLogicalEvents,
            SpineTruncated = false,
            CoverageLimited = true,
            Replay = LosslessReplayCoverageMatrix.ForLegacyObservation()
        };
    }

    public static HistoricalSegmentHeader ToHeader(
        CharacterPerformanceObservation observation,
        CharacterRecordId? canonicalCharacterRecordId,
        bool hasDurableCounterpart,
        bool usedLegacyFallback,
        string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return new HistoricalSegmentHeader
        {
            SegmentId = SegmentCaptureKey.Format(observation.GameplaySessionId, observation.SegmentOrdinal),
            CaptureKind = HistoricalCaptureKind.LegacyObservation,
            Compatibility = HistoricalCompatibility.LegacyAdapted,
            GameplaySessionId = observation.GameplaySessionId,
            SegmentOrdinal = observation.SegmentOrdinal,
            CharacterRecordId = observation.CharacterRecordId,
            CanonicalCharacterRecordId = canonicalCharacterRecordId ?? observation.CharacterRecordId,
            CaptureStartUtc = observation.StartedAtUtc,
            CaptureEndUtc = observation.EndedAtUtc,
            FinalizedAtUtc = observation.EndedAtUtc,
            ObservationSchemaVersion = observation.SchemaVersion,
            CoverageLimited = true,
            EventTimelineReplay = ReplayCoverageKind.NotRecomputable,
            HasLegacyObservationCounterpart = hasDurableCounterpart,
            UsedLegacyFallback = usedLegacyFallback,
            Detail = detail
        };
    }
}

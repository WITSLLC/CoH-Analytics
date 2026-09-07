using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Fixed-size one-second rolling earnings window retaining up to 15 minutes of history.
/// </summary>
public sealed class RollingEarningsAccumulator
{
    private readonly RollingEarningsBucket[] _buckets = new RollingEarningsBucket[RollingCombatPresets.MaximumHorizonSeconds];
    private long _endSecond;
    private bool _frozen;
    private bool _timestampPrecisionUnavailable;
    private bool _hasEarningsData;

    public long EventsAppliedToRolling { get; private set; }

    public bool TimestampPrecisionUnavailable => _timestampPrecisionUnavailable;

    public void Apply(ParserEvent parserEvent, long experienceGained, long influenceGained)
    {
        if (_frozen || (experienceGained == 0 && influenceGained == 0))
        {
            return;
        }

        if (!RollingEarningsTimestamp.TryResolveBucketSecond(parserEvent, out var bucketSecond))
        {
            _timestampPrecisionUnavailable = true;
            return;
        }

        var observedSecond = parserEvent.ObservedAt.ToUnixTimeSeconds();
        var horizonStartSecond = observedSecond - RollingCombatPresets.MaximumHorizonSeconds + 1;
        if (bucketSecond < horizonStartSecond || bucketSecond > observedSecond)
        {
            return;
        }

        AdvanceTo(observedSecond);

        ref var bucket = ref GetOrCreateBucket(bucketSecond);
        bucket.ExperienceGained += experienceGained;
        bucket.InfluenceGained += influenceGained;
        _hasEarningsData = _hasEarningsData || experienceGained > 0 || influenceGained > 0;
        EventsAppliedToRolling++;
    }

    public void Freeze()
    {
        _frozen = true;
    }

    public RollingEarningsScopeSnapshot ToSnapshot(
        DateTimeOffset sessionStartedAt,
        DateTimeOffset referenceAt,
        DateTimeOffset? timingEndAt)
    {
        var effectiveReference = timingEndAt ?? referenceAt;
        AdvanceTo(effectiveReference.ToUnixTimeSeconds());

        if (_timestampPrecisionUnavailable)
        {
            return BuildUnavailableSnapshot(RollingEarningsAvailability.UnavailableTimestampPrecision);
        }

        if (_frozen)
        {
            return BuildUnavailableSnapshot(RollingEarningsAvailability.UnavailableFrozen);
        }

        if (!_hasEarningsData)
        {
            return BuildUnavailableSnapshot(RollingEarningsAvailability.NoEarningsData);
        }

        return new RollingEarningsScopeSnapshot
        {
            TimestampPrecisionUnavailable = false,
            OneMinute = BuildWindowSnapshot(sessionStartedAt, effectiveReference, RollingCombatPresets.OneMinute),
            TwoMinutes = BuildWindowSnapshot(sessionStartedAt, effectiveReference, RollingCombatPresets.TwoMinutes),
            FiveMinutes = BuildWindowSnapshot(sessionStartedAt, effectiveReference, RollingCombatPresets.FiveMinutes),
            TenMinutes = BuildWindowSnapshot(sessionStartedAt, effectiveReference, RollingCombatPresets.TenMinutes),
            FifteenMinutes = BuildWindowSnapshot(sessionStartedAt, effectiveReference, RollingCombatPresets.FifteenMinutes)
        };
    }

    internal static int EstimateMemoryBytes() =>
        RollingCombatPresets.MaximumHorizonSeconds * RollingEarningsBucket.EstimateSizeBytes();

    private RollingEarningsScopeSnapshot BuildUnavailableSnapshot(RollingEarningsAvailability availability) =>
        new()
        {
            TimestampPrecisionUnavailable = availability == RollingEarningsAvailability.UnavailableTimestampPrecision,
            OneMinute = UnavailableWindow(RollingCombatPresets.OneMinute, availability),
            TwoMinutes = UnavailableWindow(RollingCombatPresets.TwoMinutes, availability),
            FiveMinutes = UnavailableWindow(RollingCombatPresets.FiveMinutes, availability),
            TenMinutes = UnavailableWindow(RollingCombatPresets.TenMinutes, availability),
            FifteenMinutes = UnavailableWindow(RollingCombatPresets.FifteenMinutes, availability)
        };

    private static RollingEarningsWindowSnapshot UnavailableWindow(
        int windowMinutes,
        RollingEarningsAvailability availability) =>
        new()
        {
            Availability = availability,
            WindowMinutes = windowMinutes,
            WindowDuration = TimeSpan.FromMinutes(windowMinutes)
        };

    private RollingEarningsWindowSnapshot BuildWindowSnapshot(
        DateTimeOffset sessionStartedAt,
        DateTimeOffset referenceAt,
        int windowMinutes)
    {
        var windowSeconds = windowMinutes * 60;
        var referenceSecond = referenceAt.ToUnixTimeSeconds();
        var windowStartSecond = referenceSecond - windowSeconds;
        var sessionStartSecond = sessionStartedAt.ToUnixTimeSeconds();
        var effectiveStartSecond = Math.Max(windowStartSecond, sessionStartSecond);
        var effectiveEndSecond = Math.Min(referenceSecond, _endSecond);

        if (effectiveEndSecond < effectiveStartSecond)
        {
            return new RollingEarningsWindowSnapshot
            {
                Availability = RollingEarningsAvailability.WarmingUp,
                WindowMinutes = windowMinutes,
                WindowDuration = TimeSpan.FromMinutes(windowMinutes),
                EffectiveDenominator = TimeSpan.Zero
            };
        }

        var experienceGained = 0L;
        var influenceGained = 0L;

        for (var second = effectiveStartSecond; second <= effectiveEndSecond; second++)
        {
            ref readonly var bucket = ref _buckets[IndexFor(second)];
            if (bucket.UnixSecond != second)
            {
                continue;
            }

            experienceGained += bucket.ExperienceGained;
            influenceGained += bucket.InfluenceGained;
        }

        var denominatorSeconds = Math.Min(
            windowSeconds,
            referenceSecond - sessionStartSecond);
        if (denominatorSeconds < 0)
        {
            denominatorSeconds = 0;
        }

        var denominator = TimeSpan.FromSeconds(denominatorSeconds);
        var availability = denominatorSeconds < windowSeconds
            ? RollingEarningsAvailability.WarmingUp
            : RollingEarningsAvailability.Available;

        return new RollingEarningsWindowSnapshot
        {
            Availability = availability,
            WindowMinutes = windowMinutes,
            ExperienceGained = experienceGained,
            InfluenceGained = influenceGained,
            WindowDuration = TimeSpan.FromMinutes(windowMinutes),
            EffectiveDenominator = denominator
        };
    }

    private void AdvanceTo(long targetSecond)
    {
        if (_endSecond == 0 || targetSecond > _endSecond)
        {
            _endSecond = targetSecond;
        }
    }

    private ref RollingEarningsBucket GetOrCreateBucket(long bucketSecond)
    {
        ref var bucket = ref _buckets[IndexFor(bucketSecond)];
        if (bucket.UnixSecond != bucketSecond)
        {
            bucket = new RollingEarningsBucket { UnixSecond = bucketSecond };
        }

        return ref bucket;
    }

    private static int IndexFor(long unixSecond)
    {
        var modulus = RollingCombatPresets.MaximumHorizonSeconds;
        var index = (int)(unixSecond % modulus);
        return index < 0 ? index + modulus : index;
    }

    private struct RollingEarningsBucket
    {
        public long UnixSecond;

        public long ExperienceGained;

        public long InfluenceGained;

        internal static int EstimateSizeBytes() => sizeof(long) * 3;
    }
}

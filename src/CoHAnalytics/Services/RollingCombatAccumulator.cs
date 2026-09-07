using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Fixed-size one-second rolling combat window retaining up to 15 minutes of history.
/// </summary>
public sealed class RollingCombatAccumulator
{
    private readonly RollingCombatBucket[] _buckets = new RollingCombatBucket[RollingCombatPresets.MaximumHorizonSeconds];
    private long _endSecond;
    private bool _frozen;
    private bool _timestampPrecisionUnavailable;
    private bool _hasDamageDealt;
    private bool _hasRollingData;

    public long EventsAppliedToRolling { get; private set; }

    public bool TimestampPrecisionUnavailable => _timestampPrecisionUnavailable;

    public void Apply(CombatEvent combatEvent)
    {
        if (_frozen)
        {
            return;
        }

        if (!RollingCombatTimestamp.TryResolveBucketSecond(combatEvent, out var bucketSecond))
        {
            _timestampPrecisionUnavailable = true;
            return;
        }

        var observedSecond = combatEvent.ObservedAt.ToUnixTimeSeconds();
        var horizonStartSecond = observedSecond - RollingCombatPresets.MaximumHorizonSeconds + 1;
        if (bucketSecond < horizonStartSecond || bucketSecond > observedSecond)
        {
            return;
        }

        AdvanceTo(observedSecond);

        ref var bucket = ref GetOrCreateBucket(bucketSecond);
        switch (combatEvent.Kind)
        {
            case CombatEventKind.DamageDealt:
                bucket.DamageDealtHundredths += combatEvent.Amount.Hundredths;
                _hasDamageDealt = _hasDamageDealt || combatEvent.Amount.Hundredths > 0;
                _hasRollingData = true;
                EventsAppliedToRolling++;
                break;
            case CombatEventKind.DamageReceived:
                bucket.DamageReceivedHundredths += combatEvent.Amount.Hundredths;
                _hasRollingData = true;
                EventsAppliedToRolling++;
                break;
            case CombatEventKind.Defeat:
                bucket.TotalDefeated++;
                if (combatEvent.ActorRole == CombatActorRole.Self)
                {
                    bucket.MyDefeats++;
                }
                _hasRollingData = true;
                EventsAppliedToRolling++;
                break;
            case CombatEventKind.PowerActivation:
                bucket.PowerActivations++;
                _hasRollingData = true;
                EventsAppliedToRolling++;
                break;
            case CombatEventKind.AttackResolution:
                bucket.Accuracy.Apply(combatEvent);
                _hasRollingData = true;
                EventsAppliedToRolling++;
                break;
        }
    }

    public void Freeze()
    {
        _frozen = true;
    }

    public RollingCombatScopeSnapshot ToSnapshot(
        DateTimeOffset sessionStartedAt,
        DateTimeOffset referenceAt,
        DateTimeOffset? timingEndAt)
    {
        var effectiveReference = timingEndAt ?? referenceAt;
        AdvanceTo(effectiveReference.ToUnixTimeSeconds());

        if (_timestampPrecisionUnavailable)
        {
            return BuildUnavailableSnapshot(RollingCombatAvailability.UnavailableTimestampPrecision);
        }

        if (_frozen)
        {
            return BuildUnavailableSnapshot(RollingCombatAvailability.UnavailableFrozen);
        }

        if (!_hasRollingData)
        {
            return BuildUnavailableSnapshot(RollingCombatAvailability.NoCombatData);
        }

        return new RollingCombatScopeSnapshot
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
        RollingCombatPresets.MaximumHorizonSeconds * RollingCombatBucket.EstimateSizeBytes();

    private RollingCombatScopeSnapshot BuildUnavailableSnapshot(RollingCombatAvailability availability) =>
        new()
        {
            TimestampPrecisionUnavailable = availability == RollingCombatAvailability.UnavailableTimestampPrecision,
            OneMinute = UnavailableWindow(RollingCombatPresets.OneMinute, availability),
            TwoMinutes = UnavailableWindow(RollingCombatPresets.TwoMinutes, availability),
            FiveMinutes = UnavailableWindow(RollingCombatPresets.FiveMinutes, availability),
            TenMinutes = UnavailableWindow(RollingCombatPresets.TenMinutes, availability),
            FifteenMinutes = UnavailableWindow(RollingCombatPresets.FifteenMinutes, availability)
        };

    private static RollingCombatWindowSnapshot UnavailableWindow(
        int windowMinutes,
        RollingCombatAvailability availability) =>
        new()
        {
            Availability = availability,
            WindowMinutes = windowMinutes,
            WindowDuration = TimeSpan.FromMinutes(windowMinutes)
        };

    private RollingCombatWindowSnapshot BuildWindowSnapshot(
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
            return new RollingCombatWindowSnapshot
            {
                Availability = RollingCombatAvailability.WarmingUp,
                WindowMinutes = windowMinutes,
                WindowDuration = TimeSpan.FromMinutes(windowMinutes),
                EffectiveDenominator = TimeSpan.Zero
            };
        }

        var damageHundredths = 0L;
        var receivedHundredths = 0L;
        var totalDefeated = 0L;
        var myDefeats = 0L;
        var activations = 0L;
        var accuracy = new CombatAccuracyCounterState();

        for (var second = effectiveStartSecond; second <= effectiveEndSecond; second++)
        {
            ref readonly var bucket = ref _buckets[IndexFor(second)];
            if (bucket.UnixSecond != second)
            {
                continue;
            }

            damageHundredths += bucket.DamageDealtHundredths;
            receivedHundredths += bucket.DamageReceivedHundredths;
            totalDefeated += bucket.TotalDefeated;
            myDefeats += bucket.MyDefeats;
            activations += bucket.PowerActivations;
            accuracy.Add(bucket.Accuracy);
        }

        var denominatorSeconds = Math.Min(
            windowSeconds,
            referenceSecond - sessionStartSecond);
        if (denominatorSeconds < 0)
        {
            denominatorSeconds = 0;
        }

        var denominator = TimeSpan.FromSeconds(denominatorSeconds);
        var damageDealt = new CombatScaledAmount(damageHundredths);
        var availability = denominatorSeconds < windowSeconds
            ? RollingCombatAvailability.WarmingUp
            : RollingCombatAvailability.Available;

        return new RollingCombatWindowSnapshot
        {
            Availability = availability,
            WindowMinutes = windowMinutes,
            DamageDealt = damageDealt,
            DamagePerSecondHundredths = CombatAggregator.CalculateDamagePerSecondHundredths(
                damageDealt,
                denominator),
            WindowDuration = TimeSpan.FromMinutes(windowMinutes),
            EffectiveDenominator = denominator,
            TotalDefeated = totalDefeated,
            MyDefeats = myDefeats,
            Accuracy = accuracy.ToSnapshot()
        };
    }

    private void AdvanceTo(long targetSecond)
    {
        if (_endSecond == 0 || targetSecond > _endSecond)
        {
            _endSecond = targetSecond;
        }
    }

    private ref RollingCombatBucket GetOrCreateBucket(long bucketSecond)
    {
        ref var bucket = ref _buckets[IndexFor(bucketSecond)];
        if (bucket.UnixSecond != bucketSecond)
        {
            bucket = new RollingCombatBucket { UnixSecond = bucketSecond };
        }

        return ref bucket;
    }

    private static int IndexFor(long unixSecond)
    {
        var modulus = RollingCombatPresets.MaximumHorizonSeconds;
        var index = (int)(unixSecond % modulus);
        return index < 0 ? index + modulus : index;
    }

    private struct RollingCombatBucket
    {
        public long UnixSecond;

        public long DamageDealtHundredths;

        public long DamageReceivedHundredths;

        public long TotalDefeated;

        public long MyDefeats;

        public long PowerActivations;

        public CombatAccuracyCounterState Accuracy;

        internal static int EstimateSizeBytes() => sizeof(long) * 15;
    }
}

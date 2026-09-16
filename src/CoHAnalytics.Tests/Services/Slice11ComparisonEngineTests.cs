using System.Reflection;
using System.Text.Json;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

/// <summary>Slice 11 comparison engine. No UI, ranking, rebake, or catalog rebinding.</summary>
public sealed class Slice11ComparisonEngineTests
{
    private const string HotFeet =
        "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.";
    private const string FireCagesTick =
        "2026-09-12 05:36:30 You hit Cleaner with your Fire Cages for 2.57 points of Fire damage over time.";
    private const string IncomingDamage =
        "2026-08-04 12:00:00 Crey Thorn Mook hits you with Bone Shard for 22.15 points of Lethal damage.";
    private const string PetBrawl =
        "2026-09-12 05:38:47 Imp:  You hit Cleaner with your Brawl for 15.61 points of Fire damage over time.";
    private const string PlayerBrawl =
        "2026-09-12 05:38:47 You hit Cleaner with your Brawl for 15.61 points of Fire damage.";
    private const string HealDealt =
        "2026-09-12 05:36:59 You heal Hero_A with Transfusion for 421.34 health points.";
    private const string EnduranceDealt =
        "2026-09-12 05:24:59 You hit Hero_A with your Panacea: Chance for +Hit Points/Endurance granting them 7.5 points of endurance.";
    private const string Activation =
        "2026-08-04 12:00:00 You activated the Fire Cages power.";
    private const string AttackResolution =
        "2026-08-06 12:00:00 HIT Rikti Pylon! Your Flashfire power had a 95.00% chance to hit, you rolled a 51.51.";
    private const string Defeat =
        "2026-08-04 12:00:00 You have defeated Lusca";

    private readonly ComparisonEngine _engine = new();

    [Fact]
    public void Identical_projections_have_zero_absolute_deltas()
    {
        var projection = Project(HotFeet);
        var comparison = _engine.Compare(projection, projection);
        Assert.Equal(AnalyticalComparisonCompatibility.PartiallyComparable, comparison.Compatibility);
        Assert.Equal(ComparisonState.Comparable, comparison.Session.DamageDealt.State);
        Assert.Equal(CombatScaledAmount.Zero, comparison.Session.DamageDealt.AbsoluteDelta.Value);
        Assert.Equal(0, comparison.Session.DamageDealt.PercentDeltaHundredths.Value);
        Assert.Equal(ComparisonState.Comparable, Assert.Single(comparison.Powers).DamageMagnitude.State);
        Assert.Equal(CombatScaledAmount.Zero, Assert.Single(comparison.Powers).DamageMagnitude.AbsoluteDelta.Value);
        Assert.DoesNotContain(comparison.Powers, row => row.Presence != ComparisonPresence.Matched);
    }

    [Fact]
    public void Available_zero_versus_available_ten_has_absolute_delta_without_percent()
    {
        var comparison = CompareAmounts(
            Metric<CombatScaledAmount>.Available(CombatScaledAmount.Zero),
            Metric<CombatScaledAmount>.Available(new CombatScaledAmount(10)));
        Assert.Equal(ComparisonState.Comparable, comparison.Session.DamageDealt.State);
        Assert.Equal(new CombatScaledAmount(10), comparison.Session.DamageDealt.AbsoluteDelta.Value);
        Assert.Equal(MetricAvailability.NotCaptured, comparison.Session.DamageDealt.PercentDeltaHundredths.Availability);
        Assert.Equal(ComparisonReason.None, comparison.Session.DamageDealt.Reason);
        Assert.Equal(ComparisonReason.ZeroBaseline, comparison.Session.DamageDealt.PercentDeltaReason);
        Assert.Null(comparison.Session.DamageDealt.PercentDeltaHundredths.Value);
    }

    [Fact]
    public void NotCaptured_versus_available_has_no_numeric_delta()
    {
        var comparison = CompareAmounts(
            Metric<CombatScaledAmount>.NotCaptured(),
            Metric<CombatScaledAmount>.Available(new CombatScaledAmount(10)));
        Assert.Equal(ComparisonState.Unavailable, comparison.Session.DamageDealt.State);
        Assert.Equal(ComparisonReason.NotCaptured, comparison.Session.DamageDealt.Reason);
        Assert.Equal(MetricAvailability.NotCaptured, comparison.Session.DamageDealt.AbsoluteDelta.Availability);
        Assert.Null(comparison.Session.DamageDealt.AbsoluteDelta.Value);
        Assert.Null(comparison.Session.DamageDealt.PercentDeltaHundredths.Value);
    }

    [Fact]
    public void Unsupported_versus_available_is_incompatible()
    {
        var comparison = CompareAmounts(
            Metric<CombatScaledAmount>.Unsupported(),
            Metric<CombatScaledAmount>.Available(new CombatScaledAmount(10)));
        Assert.Equal(ComparisonState.Incompatible, comparison.Session.DamageDealt.State);
        Assert.Equal(ComparisonReason.Unsupported, comparison.Session.DamageDealt.Reason);
        Assert.Null(comparison.Session.DamageDealt.AbsoluteDelta.Value);
    }

    [Fact]
    public void Incomplete_versus_available_is_partial_not_authoritative_percent()
    {
        var comparison = CompareAmounts(
            Metric<CombatScaledAmount>.Incomplete(new CombatScaledAmount(8), coverage: new CoverageInfo { MissingDamageType = true }),
            Metric<CombatScaledAmount>.Available(new CombatScaledAmount(10)));
        Assert.Equal(ComparisonState.Partial, comparison.Session.DamageDealt.State);
        Assert.Equal(ComparisonReason.Incomplete, comparison.Session.DamageDealt.Reason);
        Assert.Equal(new CombatScaledAmount(2), comparison.Session.DamageDealt.AbsoluteDelta.Value);
        Assert.Equal(MetricAvailability.Incomplete, comparison.Session.DamageDealt.AbsoluteDelta.Availability);
        Assert.Equal(MetricAvailability.Incomplete, comparison.Session.DamageDealt.PercentDeltaHundredths.Availability);
        Assert.Equal(2500, comparison.Session.DamageDealt.PercentDeltaHundredths.Value);
    }

    [Fact]
    public void Semantic_version_mismatch_blocks_numeric_comparison()
    {
        var left = WithDamage(Metric<CombatScaledAmount>.Available(new CombatScaledAmount(10)));
        var right = WithDamage(Metric<CombatScaledAmount>.Available(new CombatScaledAmount(20)), semantic: 2);
        var comparison = _engine.Compare(left, right);
        Assert.Equal(AnalyticalComparisonCompatibility.IncompatibleSemanticVersion, comparison.Compatibility);
        Assert.Equal(ComparisonState.Incompatible, comparison.Session.DamageDealt.State);
        Assert.Equal(ComparisonReason.SemanticVersionMismatch, comparison.Session.DamageDealt.Reason);
        Assert.Null(comparison.Session.DamageDealt.AbsoluteDelta.Value);
        Assert.Empty(comparison.Powers);
    }

    [Fact]
    public void Damage_dealt_comparison()
    {
        var comparison = _engine.Compare(Project(HotFeet), Project(HotFeet, HotFeet));
        Assert.Equal(ComparisonState.Comparable, comparison.Session.DamageDealt.State);
        Assert.Equal(new CombatScaledAmount(1388), comparison.Session.DamageDealt.AbsoluteDelta.Value);
        Assert.Equal(10000, comparison.Session.DamageDealt.PercentDeltaHundredths.Value);
    }

    [Fact]
    public void Damage_received_comparison()
    {
        var comparison = _engine.Compare(Project(IncomingDamage), Project(IncomingDamage, IncomingDamage));
        Assert.Equal(new CombatScaledAmount(2215), comparison.Session.DamageReceived.AbsoluteDelta.Value);
        Assert.Equal(ComparisonState.Unavailable, comparison.Session.DamageDealt.State);
    }

    [Fact]
    public void Healing_comparison()
    {
        var comparison = _engine.Compare(Project(HealDealt), Project(HealDealt, HealDealt));
        Assert.Equal(new CombatScaledAmount(42134), comparison.Session.HealingDealt.AbsoluteDelta.Value);
        Assert.Equal(ComparisonState.Unavailable, comparison.Session.DamageDealt.State);
    }

    [Fact]
    public void Endurance_comparison()
    {
        var comparison = _engine.Compare(Project(EnduranceDealt), Project(EnduranceDealt, EnduranceDealt));
        Assert.Equal(new CombatScaledAmount(750), comparison.Session.EnduranceGranted.AbsoluteDelta.Value);
    }

    [Fact]
    public void Activation_comparison()
    {
        var comparison = _engine.Compare(Project(Activation), Project(Activation, Activation));
        Assert.Equal(1, comparison.Session.ActivationCount.AbsoluteDelta.Value);
    }

    [Fact]
    public void Defeat_comparison()
    {
        var comparison = _engine.Compare(Project(Defeat), Project(Defeat, Defeat));
        Assert.Equal(1, comparison.Session.DefeatCount.AbsoluteDelta.Value);
        Assert.Equal(ComparisonState.Comparable, comparison.Session.DefeatCount.State);
    }

    [Fact]
    public void Accuracy_hit_and_miss_comparison()
    {
        var comparison = _engine.Compare(
            WithAccuracy(attempts: 4, hits: 3, misses: 1),
            WithAccuracy(attempts: 5, hits: 4, misses: 1));
        Assert.Equal(ComparisonState.Comparable, comparison.Session.Accuracy.State);
        Assert.Equal(1, comparison.Session.Accuracy.Hits.AbsoluteDelta.Value);
        Assert.Equal(0, comparison.Session.Accuracy.Misses.AbsoluteDelta.Value);
        Assert.Equal(1, comparison.Session.Accuracy.Attempts.AbsoluteDelta.Value);
    }

    [Fact]
    public void Accuracy_percentage_point_delta_is_not_relative_percent()
    {
        var comparison = _engine.Compare(
            WithAccuracy(attempts: 4, hits: 3, misses: 1),
            WithAccuracy(attempts: 5, hits: 4, misses: 1));
        // 75.00% → 80.00% = +5.00 percentage points (500 hundredths), not +6.7% relative.
        Assert.Equal(500, comparison.Session.Accuracy.HitRatePercentagePointDeltaHundredths.Value);
        Assert.Equal(MetricAvailability.Available, comparison.Session.Accuracy.HitRatePercentagePointDeltaHundredths.Availability);
    }

    [Fact]
    public void Compatible_wall_clock_rates_compare()
    {
        var start = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var capture = new SegmentClockCapture { CaptureStartUtc = start, CaptureEndUtc = start.AddSeconds(10) };
        var left = Apply(HotFeet).Project(capture);
        var right = Apply(HotFeet, HotFeet).Project(capture);
        var comparison = _engine.Compare(left, right);
        Assert.Equal(ComparisonState.Comparable, comparison.Clock.WallClockDamagePerSecondHundredths.State);
        Assert.Equal(RateDenominatorKind.WallClock, comparison.Clock.WallClockDamagePerSecondHundredths.Left.Denominator);
        Assert.Equal(RateDenominatorKind.WallClock, comparison.Clock.WallClockDamagePerSecondHundredths.Right.Denominator);
        Assert.True(comparison.Clock.WallClockDamagePerSecondHundredths.AbsoluteDelta.Value > 0);
        Assert.True(comparison.Clock.WallClockDurationsEqual);
    }

    [Fact]
    public void Different_rate_denominators_are_incompatible()
    {
        var left = WithClockRate(100, RateDenominatorKind.WallClock);
        var right = WithClockRate(200, RateDenominatorKind.TrackedPauseAdjusted);
        var comparison = _engine.Compare(left, right);
        Assert.Equal(ComparisonState.Incompatible, comparison.Clock.WallClockDamagePerSecondHundredths.State);
        Assert.Equal(ComparisonReason.DenominatorMismatch, comparison.Clock.WallClockDamagePerSecondHundredths.Reason);
        Assert.Null(comparison.Clock.WallClockDamagePerSecondHundredths.AbsoluteDelta.Value);
    }

    [Fact]
    public void Same_power_is_matched()
    {
        var comparison = _engine.Compare(Project(HotFeet), Project(HotFeet, HotFeet));
        var power = Assert.Single(comparison.Powers);
        Assert.Equal(ComparisonPresence.Matched, power.Presence);
        Assert.Equal("Hot Feet", power.Key.PowerName);
        Assert.Equal(CombatAnalyticsScope.Self, power.Key.Scope);
        Assert.Equal(new CombatScaledAmount(1388), power.DamageMagnitude.AbsoluteDelta.Value);
    }

    [Fact]
    public void Player_and_pet_same_name_powers_do_not_merge()
    {
        var both = Project(PlayerBrawl, PetBrawl);
        var comparison = _engine.Compare(both, both);
        Assert.Contains(comparison.Powers, row => row.Key is { Scope: CombatAnalyticsScope.Self, PowerName: "Brawl" });
        Assert.Contains(comparison.Powers, row => row.Key.Scope == CombatAnalyticsScope.PerPet && row.Key.PowerName == "Brawl");
        var self = Assert.Single(comparison.Powers, row => row.Key.Scope == CombatAnalyticsScope.Self && row.Key.PowerName == "Brawl");
        var pet = Assert.Single(comparison.Powers, row => row.Key.Scope == CombatAnalyticsScope.PerPet && row.Key.PowerName == "Brawl");
        Assert.NotEqual(self.Key.PetNormalizedName, pet.Key.PetNormalizedName);
        Assert.Equal(ComparisonPresence.Matched, self.Presence);
        Assert.Equal(ComparisonPresence.Matched, pet.Presence);
    }

    [Fact]
    public void Left_only_power_is_exposed()
    {
        var comparison = _engine.Compare(Project(HotFeet, FireCagesTick), Project(HotFeet));
        var extra = Assert.Single(comparison.Powers, row => row.Key.PowerName == "Fire Cages");
        Assert.Equal(ComparisonPresence.LeftOnly, extra.Presence);
        Assert.NotNull(extra.Left);
        Assert.Null(extra.Right);
        Assert.Equal(ComparisonState.Unavailable, extra.DamageMagnitude.State);
        Assert.Equal(ComparisonReason.LeftOnly, extra.DamageMagnitude.Reason);
    }

    [Fact]
    public void Right_only_power_is_exposed()
    {
        var comparison = _engine.Compare(Project(HotFeet), Project(HotFeet, FireCagesTick));
        var extra = Assert.Single(comparison.Powers, row => row.Key.PowerName == "Fire Cages");
        Assert.Equal(ComparisonPresence.RightOnly, extra.Presence);
        Assert.Equal(ComparisonReason.RightOnly, extra.DamageMagnitude.Reason);
    }

    [Fact]
    public void Direct_versus_dot_comparison()
    {
        var comparison = _engine.Compare(Project(HotFeet), Project(FireCagesTick));
        var left = Assert.Single(comparison.Powers, row => row.Presence == ComparisonPresence.LeftOnly);
        var right = Assert.Single(comparison.Powers, row => row.Presence == ComparisonPresence.RightOnly);
        Assert.Equal(new CombatScaledAmount(1388), left.Left!.DirectAmount);
        Assert.Equal(CombatScaledAmount.Zero, left.Left.DotAmount);
        Assert.Equal(new CombatScaledAmount(257), right.Right!.DotAmount);
        Assert.Equal(CombatScaledAmount.Zero, right.Right.DirectAmount);

        var matched = _engine.Compare(Project(HotFeet), Project(HotFeet, FireCagesTick));
        var hotFeet = Assert.Single(matched.Powers, row => row.Key.PowerName == "Hot Feet");
        Assert.Equal(ComparisonState.Comparable, hotFeet.DirectAmount.State);
        Assert.Equal(CombatScaledAmount.Zero, hotFeet.DirectAmount.AbsoluteDelta.Value);
        Assert.Equal(ComparisonState.Comparable, hotFeet.DotAmount.State);
    }

    [Fact]
    public void Damage_type_comparison_with_complete_coverage_treats_missing_as_zero()
    {
        var fire = Project(HotFeet);
        var fireAndLethal = Project(HotFeet, IncomingDamage);
        Assert.Equal(MetricAvailability.Available, fire.DamageTypeBreakdown.Availability);
        var comparison = _engine.Compare(fire, fireAndLethal);
        var fireType = Assert.Single(comparison.OutgoingDamageTypes);
        Assert.Equal(ComparisonPresence.Matched, fireType.Presence);
        Assert.Equal(CombatScaledAmount.Zero, fireType.Amount.AbsoluteDelta.Value);
        Assert.Contains(comparison.IncomingDamageTypes, row => row.DamageType.Text == "Lethal");
    }

    [Fact]
    public void Missing_damage_type_under_incomplete_coverage_is_not_zero()
    {
        var fire = Assert.Single(ParseCanonical(HotFeet));
        var energy = Assert.Single(ParseCanonical(
            "2026-09-12 05:42:17 You hit Sweeper with your Doublehit for 67.04 points of Energy damage."));
        var incomplete = new CombatEngine();
        incomplete.Apply(fire with { DamageType = null });
        incomplete.Apply(fire);
        var complete = new CombatEngine();
        complete.Apply(fire);
        complete.Apply(energy);
        var comparison = _engine.Compare(incomplete.Project(), complete.Project());
        Assert.Equal(MetricAvailability.Incomplete, incomplete.Project().DamageTypeBreakdown.Availability);
        Assert.Equal(MetricAvailability.Available, complete.Project().DamageTypeBreakdown.Availability);
        var energyType = Assert.Single(comparison.OutgoingDamageTypes, row => row.DamageType.Text == "Energy");
        Assert.Equal(ComparisonPresence.RightOnly, energyType.Presence);
        Assert.Equal(ComparisonState.Unavailable, energyType.Amount.State);
        Assert.Equal(ComparisonReason.MissingDamageType, energyType.Amount.Reason);
        Assert.Null(energyType.Amount.AbsoluteDelta.Value);
    }

    [Fact]
    public void Pet_aggregate_comparison_preserves_coverage_limited()
    {
        var comparison = _engine.Compare(Project(PetBrawl), Project(PetBrawl, PetBrawl));
        var pets = Assert.Single(comparison.Actors, row => row.Scope == CombatAnalyticsScope.OwnPetsAggregate);
        Assert.True(pets.CoverageLimited);
        Assert.Equal(ComparisonState.Partial, pets.DamageDealt.State);
        Assert.Equal(ComparisonState.Incompatible, pets.PetInstanceCount.State);
        Assert.Equal(ComparisonReason.Unsupported, pets.PetInstanceCount.Reason);
        Assert.Equal(new CombatScaledAmount(1561), pets.DamageDealt.AbsoluteDelta.Value);
    }

    [Fact]
    public void Target_lower_bound_prevents_exact_cardinality_comparison()
    {
        var baseline = Assert.Single(ParseCanonical(HotFeet));
        var overflowing = new CombatEngine();
        for (var index = 0; index < CombatEngine.MaxTrackedTargets + 1; index++)
        {
            overflowing.Apply(baseline with
            {
                Target = ActorRef.UnknownNamed("Target " + index)
            });
        }

        var small = Project(HotFeet);
        var comparison = _engine.Compare(small, overflowing.Project());
        Assert.False(comparison.ExactTargetCardinalityComparable);
        Assert.Equal(ComparisonState.Partial, comparison.Session.DistinctTargetCount.State);
        Assert.Contains(comparison.Targets, row => row.Presence == ComparisonPresence.RightOnly);
        Assert.All(
            comparison.Targets.Where(row => row.Presence != ComparisonPresence.Matched),
            row => Assert.True(row.DamageDealt.Reason is ComparisonReason.TargetLowerBound or ComparisonReason.Overflow));
    }

    [Fact]
    public void Build_fingerprint_same_and_different()
    {
        var same = _engine.Compare(
            WithBuild("hash-a", "catalog-a"),
            WithBuild("hash-a", "catalog-a"));
        Assert.True(same.Build.SameManifestHash);
        Assert.True(same.Build.SameCatalogFingerprint);

        var different = _engine.Compare(
            WithBuild("hash-a", "catalog-a"),
            WithBuild("hash-b", "catalog-b"));
        Assert.False(different.Build.SameManifestHash);
        Assert.False(different.Build.SameCatalogFingerprint);
        Assert.Equal(CombatScaledAmount.Zero, different.Session.DamageDealt.AbsoluteDelta.Value);
    }

    [Fact]
    public void Attribution_comparison_same_policy()
    {
        var comparison = _engine.Compare(
            WithAttribution(policy: 1, unattributed: 400, proc: 200),
            WithAttribution(policy: 1, unattributed: 500, proc: 250));
        Assert.Equal(ComparisonState.Comparable, comparison.Attribution.State);
        Assert.Equal(new CombatScaledAmount(100), comparison.Attribution.UnattributedDamage.AbsoluteDelta.Value);
        Assert.Equal(new CombatScaledAmount(50), comparison.Attribution.ProcDamage.AbsoluteDelta.Value);
        Assert.Equal(1, comparison.Attribution.LeftPolicyVersion);
        Assert.Equal(1, comparison.Attribution.RightPolicyVersion);
    }

    [Fact]
    public void Attribution_policy_mismatch_is_incompatible_without_rerun()
    {
        var comparison = _engine.Compare(
            WithAttribution(policy: 1, unattributed: 400, proc: 200),
            WithAttribution(policy: 2, unattributed: 500, proc: 250));
        Assert.Equal(ComparisonState.Incompatible, comparison.Attribution.State);
        Assert.Equal(ComparisonReason.AttributionPolicyMismatch, comparison.Attribution.Reason);
        Assert.Null(comparison.Attribution.ProcDamage.AbsoluteDelta.Value);
        Assert.Null(comparison.Attribution.UnattributedDamage.AbsoluteDelta.Value);
    }

    [Fact]
    public void Unattributed_amount_comparison()
    {
        var comparison = _engine.Compare(
            WithAttribution(policy: 1, unattributed: 0, proc: 100),
            WithAttribution(policy: 1, unattributed: 80, proc: 100));
        Assert.Equal(new CombatScaledAmount(80), comparison.Attribution.UnattributedDamage.AbsoluteDelta.Value);
        Assert.Equal(ComparisonState.Comparable, comparison.Attribution.UnattributedDamage.State);
    }

    [Fact]
    public void Legacy_versus_durable_compares_only_compatible_metrics()
    {
        var durable = Project(HotFeet, IncomingDamage, HealDealt);
        var legacy = LegacyObservationAdapter.ToProjection(new CharacterPerformanceObservation
        {
            SchemaVersion = 2,
            GameplaySessionId = GameplaySessionId.CreateNew(),
            SegmentOrdinal = 0,
            CharacterRecordId = CharacterRecordId.CreateNew(),
            StartedAtUtc = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            EndedAtUtc = new DateTimeOffset(2026, 8, 4, 12, 30, 0, TimeSpan.Zero),
            DamageDealt = durable.Session.DamageDealt,
            Attempts = 4,
            Hits = 3,
            TotalDefeated = 1
        });
        var comparison = _engine.Compare(
            new AnalyticalProjectionView
            {
                SourceKind = AnalyticalProjectionSourceKind.HistoricalLegacy,
                Projection = legacy
            },
            new AnalyticalProjectionView
            {
                SourceKind = AnalyticalProjectionSourceKind.HistoricalDurable,
                Projection = durable
            });
        Assert.Equal(AnalyticalComparisonCompatibility.PartiallyComparable, comparison.Compatibility);
        Assert.Equal(ComparisonState.Comparable, comparison.Session.DamageDealt.State);
        Assert.Equal(CombatScaledAmount.Zero, comparison.Session.DamageDealt.AbsoluteDelta.Value);
        Assert.Equal(ComparisonState.Unavailable, comparison.Session.DamageReceived.State);
        Assert.Equal(ComparisonState.Unavailable, comparison.Session.HealingDealt.State);
        Assert.Equal(ComparisonReason.NotCaptured, comparison.Session.HealingDealt.Reason);
        Assert.Equal(ComparisonState.Unavailable, comparison.Clock.WallClockDamagePerSecondHundredths.State);
        Assert.Equal(AnalyticalProjectionSourceKind.HistoricalLegacy, comparison.Left.SourceKind);
        Assert.Equal(AnalyticalProjectionSourceKind.HistoricalDurable, comparison.Right.SourceKind);
    }

    [Fact]
    public void Legacy_missing_dimensions_remain_unavailable()
    {
        var legacy = LegacyObservationAdapter.ToProjection(new CharacterPerformanceObservation
        {
            SchemaVersion = 2,
            GameplaySessionId = GameplaySessionId.CreateNew(),
            SegmentOrdinal = 0,
            CharacterRecordId = CharacterRecordId.CreateNew(),
            StartedAtUtc = DateTimeOffset.UnixEpoch,
            EndedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1),
            DamageDealt = new CombatScaledAmount(100)
        });
        var comparison = _engine.Compare(legacy, legacy);
        Assert.Equal(ComparisonState.Unavailable, comparison.Session.DamageReceived.State);
        Assert.Equal(ComparisonState.Unavailable, comparison.Session.HealingDealt.State);
        Assert.Equal(ComparisonState.Unavailable, comparison.Session.EnduranceGranted.State);
        Assert.Empty(comparison.Powers);
        Assert.Equal(MetricAvailability.NotCaptured, comparison.Build.LeftAvailability);
        Assert.Null(comparison.Build.SameManifestHash);
    }

    [Fact]
    public void Live_versus_historical_uses_the_same_engine()
    {
        var projection = Project(HotFeet);
        var comparison = _engine.Compare(
            new AnalyticalProjectionView
            {
                SourceKind = AnalyticalProjectionSourceKind.Live,
                Projection = projection
            },
            new AnalyticalProjectionView
            {
                SourceKind = AnalyticalProjectionSourceKind.HistoricalDurable,
                Projection = projection,
                Header = new HistoricalSegmentHeader
                {
                    SegmentId = "live-vs-hist",
                    CaptureKind = HistoricalCaptureKind.DurableSegment,
                    Compatibility = HistoricalCompatibility.AuthoritativeAggregate,
                    GameplaySessionId = GameplaySessionId.CreateNew(),
                    SegmentOrdinal = 0,
                    CaptureStartUtc = DateTimeOffset.UnixEpoch,
                    CaptureEndUtc = DateTimeOffset.UnixEpoch.AddMinutes(1),
                    AnalyticsSemanticVersion = 3
                }
            });
        Assert.Equal(AnalyticalProjectionSourceKind.Live, comparison.Left.SourceKind);
        Assert.Equal(AnalyticalProjectionSourceKind.HistoricalDurable, comparison.Right.SourceKind);
        Assert.Equal(CombatScaledAmount.Zero, comparison.Session.DamageDealt.AbsoluteDelta.Value);
        Assert.Equal("live-vs-hist", comparison.Right.SegmentId);
    }

    [Fact]
    public void Historical_versus_historical_works()
    {
        var comparison = _engine.Compare(
            new AnalyticalProjectionView
            {
                SourceKind = AnalyticalProjectionSourceKind.HistoricalDurable,
                Projection = Project(HotFeet)
            },
            new AnalyticalProjectionView
            {
                SourceKind = AnalyticalProjectionSourceKind.HistoricalDurable,
                Projection = Project(HotFeet, HotFeet)
            });
        Assert.Equal(AnalyticalProjectionSourceKind.HistoricalDurable, comparison.Left.SourceKind);
        Assert.Equal(AnalyticalProjectionSourceKind.HistoricalDurable, comparison.Right.SourceKind);
        Assert.Equal(new CombatScaledAmount(1388), comparison.Session.DamageDealt.AbsoluteDelta.Value);
    }

    [Fact]
    public void Comparison_engine_does_not_access_catalog_or_replay_engine_or_store()
    {
        var catalogPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "CoHAnalytics",
            "ReferenceData",
            "item-catalog.v1.json"));
        var before = File.ReadAllBytes(catalogPath);
        _engine.Compare(Project(HotFeet), Project(HotFeet, IncomingDamage));
        Assert.Equal(before, File.ReadAllBytes(catalogPath));
        var fields = typeof(ComparisonEngine).GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.DoesNotContain(fields, field =>
            field.FieldType.Name.Contains("CombatEngine", StringComparison.Ordinal)
            || field.FieldType.Name.Contains("IItemReferenceCatalog", StringComparison.Ordinal)
            || field.FieldType.Name.Contains("ISegmentStore", StringComparison.Ordinal)
            || field.FieldType.Name.Contains("ProcAttributionClassifier", StringComparison.Ordinal)
            || field.FieldType.Name.Contains("ICharacterBuildSnapshotStore", StringComparison.Ordinal));
    }

    [Fact]
    public void Power_order_is_deterministic()
    {
        var mixed = Project(FireCagesTick, HotFeet, PetBrawl);
        var first = _engine.Compare(mixed, mixed).Powers.Select(row => (row.Key.Direction, row.Key.Scope, row.Key.PetNormalizedName, row.Key.PowerName)).ToArray();
        var second = _engine.Compare(mixed, mixed).Powers.Select(row => (row.Key.Direction, row.Key.Scope, row.Key.PetNormalizedName, row.Key.PowerName)).ToArray();
        Assert.Equal(first, second);
        for (var index = 1; index < first.Length; index++)
        {
            Assert.True(Comparer<(CombatAnalyticsDirection, CombatAnalyticsScope, string, string)>.Default.Compare(first[index - 1], first[index]) <= 0);
        }
    }

    [Fact]
    public void Percentage_math_is_overflow_safe()
    {
        var comparison = CompareAmounts(
            Metric<CombatScaledAmount>.Available(new CombatScaledAmount(1)),
            Metric<CombatScaledAmount>.Available(new CombatScaledAmount(long.MaxValue / 2)));
        Assert.Equal(ComparisonState.Comparable, comparison.Session.DamageDealt.State);
        Assert.Equal(new CombatScaledAmount((long.MaxValue / 2) - 1), comparison.Session.DamageDealt.AbsoluteDelta.Value);
        Assert.True(
            comparison.Session.DamageDealt.PercentDeltaHundredths.Availability
                is MetricAvailability.Available
                or MetricAvailability.NotCaptured);
    }

    [Fact]
    public void Source_projections_are_unchanged_after_comparison()
    {
        var left = Project(HotFeet);
        var right = Project(HotFeet, IncomingDamage);
        var leftDamage = left.Session.DamageDealt;
        var rightReceived = right.Session.DamageReceived;
        var leftPowers = left.Powers;
        _engine.Compare(left, right);
        Assert.Equal(leftDamage, left.Session.DamageDealt);
        Assert.Equal(rightReceived, right.Session.DamageReceived);
        Assert.Same(leftPowers, left.Powers);
        Assert.Equal(AnalyticsSemanticVersion.Current, left.AnalyticsSemanticVersion);
        Assert.Equal(3, AnalyticsSemanticVersion.Current);
    }

    [Fact]
    public void Available_zero_versus_available_zero_has_zero_delta_without_percent()
    {
        var comparison = CompareAmounts(
            Metric<CombatScaledAmount>.Available(CombatScaledAmount.Zero),
            Metric<CombatScaledAmount>.Available(CombatScaledAmount.Zero));
        Assert.Equal(CombatScaledAmount.Zero, comparison.Session.DamageDealt.AbsoluteDelta.Value);
        Assert.Equal(MetricAvailability.NotCaptured, comparison.Session.DamageDealt.PercentDeltaHundredths.Availability);
        Assert.Equal(ComparisonReason.ZeroBaseline, comparison.Session.DamageDealt.PercentDeltaReason);
    }

    [Fact]
    public void Equal_current_semantics_with_complete_build_context_are_comparable()
    {
        var comparison = _engine.Compare(WithBuild("hash-a", "catalog-a"), WithBuild("hash-a", "catalog-a"));
        Assert.Equal(AnalyticalComparisonCompatibility.Comparable, comparison.Compatibility);
    }

    [Fact]
    public void Equal_old_semantic_versions_are_not_silently_accepted()
    {
        var left = WithDamage(Metric<CombatScaledAmount>.Available(new CombatScaledAmount(10)), semantic: 2);
        var right = WithDamage(Metric<CombatScaledAmount>.Available(new CombatScaledAmount(20)), semantic: 2);
        var comparison = _engine.Compare(left, right);
        Assert.Equal(AnalyticalComparisonCompatibility.IncompatibleSemanticVersion, comparison.Compatibility);
        Assert.Equal(ComparisonState.Incompatible, comparison.Session.DamageDealt.State);
    }

    [Fact]
    public void Explicit_unknown_semantic_version_is_rejected()
    {
        var unknown = WithDamage(
            Metric<CombatScaledAmount>.Available(new CombatScaledAmount(1)),
            semantic: 0);
        var comparison = _engine.Compare(
            unknown,
            WithDamage(Metric<CombatScaledAmount>.Available(new CombatScaledAmount(1))));
        Assert.Equal(AnalyticalComparisonCompatibility.IncompatibleSemanticVersion, comparison.Compatibility);
        Assert.Equal(ComparisonState.Incompatible, comparison.Clock.ObservedAnalyticalSpan.State);
        Assert.Equal(ComparisonState.Incompatible, comparison.Attribution.UnattributedDamage.State);
    }

    [Fact]
    public void Unrelated_combat_does_not_fabricate_observed_zero_defeats()
    {
        var comparison = _engine.Compare(Project(HotFeet), Project(HotFeet, HotFeet));
        Assert.Equal(ComparisonState.Unavailable, comparison.Session.DefeatCount.State);
        Assert.Equal(ComparisonReason.NotCaptured, comparison.Session.DefeatCount.Reason);
    }

    [Fact]
    public void Legacy_defeats_compare_only_in_legacy_source_scope()
    {
        var left = LegacyView(totalDefeated: 0);
        var right = LegacyView(totalDefeated: 2);
        var comparison = _engine.Compare(left, right);
        Assert.Equal(ComparisonState.Comparable, comparison.Session.DefeatCount.State);
        Assert.Equal(2, comparison.Session.DefeatCount.AbsoluteDelta.Value);
    }

    [Fact]
    public void Negative_delta_and_percentage_round_toward_zero()
    {
        var negative = CompareAmounts(
            Metric<CombatScaledAmount>.Available(new CombatScaledAmount(10)),
            Metric<CombatScaledAmount>.Available(new CombatScaledAmount(5)));
        Assert.Equal(new CombatScaledAmount(-5), negative.Session.DamageDealt.AbsoluteDelta.Value);
        Assert.Equal(-5000, negative.Session.DamageDealt.PercentDeltaHundredths.Value);

        var rounded = CompareAmounts(
            Metric<CombatScaledAmount>.Available(new CombatScaledAmount(3)),
            Metric<CombatScaledAmount>.Available(new CombatScaledAmount(4)));
        Assert.Equal(3333, rounded.Session.DamageDealt.PercentDeltaHundredths.Value);
    }

    [Fact]
    public void Extreme_long_delta_is_unavailable_instead_of_throwing()
    {
        var comparison = _engine.Compare(
            WithDamageEventCount(long.MinValue),
            WithDamageEventCount(long.MaxValue));
        Assert.Equal(ComparisonState.Unavailable, comparison.Session.DamageEventCount.State);
        Assert.Equal(ComparisonReason.ArithmeticOverflow, comparison.Session.DamageEventCount.Reason);
        Assert.Null(comparison.Session.DamageEventCount.AbsoluteDelta.Value);
    }

    [Fact]
    public void Long_minimum_percent_baseline_does_not_overflow()
    {
        var comparison = _engine.Compare(
            WithDamageEventCount(long.MinValue),
            WithDamageEventCount(long.MinValue));
        Assert.Equal(ComparisonState.Comparable, comparison.Session.DamageEventCount.State);
        Assert.Equal(0, comparison.Session.DamageEventCount.PercentDeltaHundredths.Value);
    }

    [Fact]
    public void Duplicate_power_keys_are_preserved_as_ambiguous()
    {
        var row = new CombatPowerAnalysisRow
        {
            Scope = CombatAnalyticsScope.Self,
            PowerName = "Hasten",
            ActivationCount = 1
        };
        var left = WithDamage(Metric<CombatScaledAmount>.Available(new CombatScaledAmount(1))) with
        {
            Powers = [row, row with { ActivationCount = 2 }]
        };
        var right = WithDamage(Metric<CombatScaledAmount>.Available(new CombatScaledAmount(1))) with
        {
            Powers = [row]
        };

        var power = Assert.Single(_engine.Compare(left, right).Powers);
        Assert.Equal(ComparisonPresence.Ambiguous, power.Presence);
        Assert.Equal(2, power.LeftCandidates.Count);
        Assert.Single(power.RightCandidates);
        Assert.Equal(ComparisonReason.DuplicateKey, power.DamageMagnitude.Reason);
    }

    [Fact]
    public void Literal_other_power_does_not_collide_with_overflow_bucket()
    {
        var literal = new CombatPowerAnalysisRow { Scope = CombatAnalyticsScope.Self, PowerName = "Other" };
        var overflow = literal with { IsOverflow = true, CoverageLimited = true };
        var projection = WithDamage(Metric<CombatScaledAmount>.Available(new CombatScaledAmount(1))) with
        {
            Powers = [overflow, literal]
        };
        var powers = _engine.Compare(projection, projection).Powers;
        Assert.Equal(2, powers.Count);
        Assert.Contains(powers, row => row.Key.IsOverflow);
        Assert.Contains(powers, row => !row.Key.IsOverflow);
    }

    [Fact]
    public void Zero_attempt_accuracy_has_no_fabricated_rate()
    {
        var comparison = _engine.Compare(WithAccuracy(0, 0, 0), WithAccuracy(0, 0, 0));
        Assert.Equal(ComparisonState.Comparable, comparison.Session.Accuracy.State);
        Assert.Equal(ComparisonReason.ZeroBaseline, comparison.Session.Accuracy.HitRatePercentagePointReason);
        Assert.Null(comparison.Session.Accuracy.HitRatePercentagePointDeltaHundredths.Value);
    }

    [Fact]
    public void Extreme_accuracy_percentage_point_delta_is_unavailable_instead_of_overflowing()
    {
        var comparison = _engine.Compare(
            WithAccuracy(1, 0, 1),
            WithAccuracy(1, long.MaxValue / 2, 0));
        Assert.Equal(ComparisonState.Comparable, comparison.Session.Accuracy.State);
        Assert.Equal(
            ComparisonReason.ArithmeticOverflow,
            comparison.Session.Accuracy.HitRatePercentagePointReason);
        Assert.Null(comparison.Session.Accuracy.HitRatePercentagePointDeltaHundredths.Value);
    }

    [Fact]
    public void Source_projection_graphs_are_deeply_unchanged()
    {
        var left = Project(HotFeet, FireCagesTick, PetBrawl);
        var right = Project(HotFeet, IncomingDamage, HealDealt);
        var leftBefore = JsonSerializer.Serialize(left);
        var rightBefore = JsonSerializer.Serialize(right);
        _engine.Compare(left, right);
        Assert.Equal(leftBefore, JsonSerializer.Serialize(left));
        Assert.Equal(rightBefore, JsonSerializer.Serialize(right));
    }

    [Fact]
    public void Correlated_attribution_bucket_remains_unsupported_under_policy_one()
    {
        var comparison = _engine.Compare(
            WithAttribution(policy: 1, unattributed: 10, proc: 1),
            WithAttribution(policy: 1, unattributed: 20, proc: 2));
        Assert.Equal(ComparisonState.Incompatible, comparison.Attribution.CorrelatedCount.State);
        Assert.Equal(ComparisonReason.Unsupported, comparison.Attribution.CorrelatedCount.Reason);
        Assert.Null(comparison.Attribution.CorrelatedCount.AbsoluteDelta.Value);
    }

    [Fact]
    public void Incoming_and_outgoing_damage_type_completeness_are_independent()
    {
        var fire = new CombatDamageTypeTotal
        {
            DamageType = new DamageType("Fire"),
            Amount = new CombatScaledAmount(100),
            EventCount = 1
        };
        var energy = new CombatDamageTypeTotal
        {
            DamageType = new DamageType("Energy"),
            Amount = new CombatScaledAmount(200),
            EventCount = 1
        };
        var baseProjection = WithDamage(Metric<CombatScaledAmount>.Available(new CombatScaledAmount(1)));
        var left = baseProjection with
        {
            DamageTypes = [fire],
            DamageTypeBreakdown = MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.Available([fire]),
            IncomingDamageTypes = [fire],
            IncomingDamageTypeBreakdown = MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.Incomplete(
                [fire],
                coverage: new CoverageInfo { MissingDamageType = true, LowerBound = true })
        };
        var right = baseProjection with
        {
            DamageTypes = [fire, energy],
            DamageTypeBreakdown = MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.Available([fire, energy]),
            IncomingDamageTypes = [fire, energy],
            IncomingDamageTypeBreakdown = MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.Available([fire, energy])
        };

        var comparison = _engine.Compare(left, right);
        var outgoingEnergy = Assert.Single(comparison.OutgoingDamageTypes, row => row.DamageType.Text == "Energy");
        var incomingEnergy = Assert.Single(comparison.IncomingDamageTypes, row => row.DamageType.Text == "Energy");
        Assert.Equal(ComparisonState.Comparable, outgoingEnergy.Amount.State);
        Assert.Equal(new CombatScaledAmount(200), outgoingEnergy.Amount.AbsoluteDelta.Value);
        Assert.Equal(ComparisonState.Unavailable, incomingEnergy.Amount.State);
        Assert.Equal(ComparisonReason.MissingDamageType, incomingEnergy.Amount.Reason);
    }

    [Fact]
    public void Source_collection_order_does_not_change_result_order()
    {
        var projection = Project(HotFeet, FireCagesTick, PetBrawl, IncomingDamage);
        var reversed = projection with
        {
            Powers = projection.Powers.Reverse().ToArray(),
            DamageTypes = projection.DamageTypes.Reverse().ToArray(),
            IncomingDamageTypes = projection.IncomingDamageTypes.Reverse().ToArray(),
            Actors = projection.Actors.Reverse().ToArray(),
            Targets = projection.Targets.Reverse().ToArray()
        };
        var original = _engine.Compare(projection, projection);
        var reordered = _engine.Compare(reversed, reversed);
        Assert.Equal(original.Powers.Select(row => row.Key), reordered.Powers.Select(row => row.Key));
        Assert.Equal(original.Actors.Select(row => (row.Scope, row.PetNormalizedName, row.IsOverflow)), reordered.Actors.Select(row => (row.Scope, row.PetNormalizedName, row.IsOverflow)));
        Assert.Equal(original.OutgoingDamageTypes.Select(row => (row.DamageType, row.IsOverflow)), reordered.OutgoingDamageTypes.Select(row => (row.DamageType, row.IsOverflow)));
        Assert.Equal(original.Targets.Select(row => (row.NormalizedTargetName, row.Overflow)), reordered.Targets.Select(row => (row.NormalizedTargetName, row.Overflow)));
    }

    [Fact]
    public void Different_durations_do_not_block_raw_totals()
    {
        var start = DateTimeOffset.UnixEpoch;
        var left = Apply(HotFeet).Project(new SegmentClockCapture { CaptureStartUtc = start, CaptureEndUtc = start.AddSeconds(10) });
        var right = Apply(HotFeet, HotFeet).Project(new SegmentClockCapture { CaptureStartUtc = start, CaptureEndUtc = start.AddSeconds(20) });
        var comparison = _engine.Compare(left, right);
        Assert.False(comparison.Clock.WallClockDurationsEqual);
        Assert.Equal(ComparisonState.Comparable, comparison.Session.DamageDealt.State);
        Assert.Equal(new CombatScaledAmount(1388), comparison.Session.DamageDealt.AbsoluteDelta.Value);
    }

    [Fact]
    public void Missing_build_hash_is_unknown_and_makes_overall_partial()
    {
        var left = WithBuild("hash-a", "catalog-a");
        var right = left with { BuildContext = CombatBuildContextSummary.NotCaptured };
        var comparison = _engine.Compare(left, right);
        Assert.Null(comparison.Build.SameManifestHash);
        Assert.Null(comparison.Build.SameCatalogFingerprint);
        Assert.Equal(AnalyticalComparisonCompatibility.PartiallyComparable, comparison.Compatibility);
    }

    [Fact]
    public void Attribution_policy_mismatch_keeps_non_attribution_totals_and_overall_is_partial()
    {
        var comparison = _engine.Compare(
            WithAttribution(policy: 1, unattributed: 10, proc: 2),
            WithAttribution(policy: 2, unattributed: 20, proc: 3));
        Assert.Equal(ComparisonState.Comparable, comparison.Session.DamageDealt.State);
        Assert.Equal(ComparisonState.Incompatible, comparison.Attribution.State);
        Assert.Equal(AnalyticalComparisonCompatibility.PartiallyComparable, comparison.Compatibility);
    }

    [Fact]
    public void Historical_header_semantic_mismatch_blocks_projection_values()
    {
        var projection = WithDamage(Metric<CombatScaledAmount>.Available(new CombatScaledAmount(1)));
        var left = DurableView(projection, semantic: 3, attributionPolicy: 1);
        var right = DurableView(projection, semantic: 4, attributionPolicy: 1);
        var comparison = _engine.Compare(left, right);
        Assert.Equal(AnalyticalComparisonCompatibility.IncompatibleSemanticVersion, comparison.Compatibility);
        Assert.Null(comparison.Session.DamageDealt.AbsoluteDelta.Value);
    }

    [Fact]
    public void Historical_header_attribution_policy_mismatch_blocks_only_attribution()
    {
        var projection = WithAttribution(policy: 1, unattributed: 10, proc: 2);
        var left = DurableView(projection, semantic: 3, attributionPolicy: 1);
        var right = DurableView(projection, semantic: 3, attributionPolicy: 2);
        var comparison = _engine.Compare(left, right);
        Assert.Equal(ComparisonState.Comparable, comparison.Session.DamageDealt.State);
        Assert.Equal(ComparisonReason.AttributionPolicyMismatch, comparison.Attribution.Reason);
        Assert.Equal(1, comparison.Attribution.LeftPolicyVersion);
        Assert.Equal(2, comparison.Attribution.RightPolicyVersion);
    }

    [Fact]
    public void Pet_name_matching_uses_upstream_case_insensitive_rollup_identity()
    {
        var lower = new CombatPowerAnalysisRow
        {
            Scope = CombatAnalyticsScope.PerPet,
            PowerName = "Brawl",
            PetNormalizedName = "imp",
            CoverageLimited = true
        };
        var upper = lower with { PetNormalizedName = "IMP" };
        var baseProjection = WithDamage(Metric<CombatScaledAmount>.Available(new CombatScaledAmount(1)));
        var comparison = _engine.Compare(
            baseProjection with { Powers = [lower] },
            baseProjection with { Powers = [upper] });
        var power = Assert.Single(comparison.Powers);
        Assert.Equal(ComparisonPresence.Matched, power.Presence);
        Assert.Equal("IMP", power.Key.PetNormalizedName);
    }

    [Fact]
    public void Comparison_state_has_no_unimplemented_normalized_member()
    {
        Assert.DoesNotContain("Normalized", Enum.GetNames<ComparisonState>());
    }

    [Fact]
    public void Legacy_wall_clock_uses_the_same_capture_boundary_meaning_as_durable_wall_clock()
    {
        var legacy = LegacyView(totalDefeated: 0);
        var start = DateTimeOffset.UnixEpoch;
        var durableProjection = Apply(HotFeet).Project(new SegmentClockCapture
        {
            CaptureStartUtc = start,
            CaptureEndUtc = start.AddMinutes(1)
        });
        var durable = DurableView(durableProjection, semantic: 3, attributionPolicy: 1);
        var comparison = _engine.Compare(legacy, durable);
        Assert.Equal(ComparisonState.Comparable, comparison.Clock.WallClockDuration.State);
        Assert.Equal(TimeSpan.Zero, comparison.Clock.WallClockDuration.AbsoluteDelta.Value);
        Assert.Equal(RateDenominatorKind.WallClock, comparison.Clock.WallClockDuration.Left.Denominator);
        Assert.Equal(RateDenominatorKind.WallClock, comparison.Clock.WallClockDuration.Right.Denominator);
    }

    private AnalyticalComparison CompareAmounts(Metric<CombatScaledAmount> left, Metric<CombatScaledAmount> right) =>
        _engine.Compare(WithDamage(left), WithDamage(right));

    private static CombatAnalyticsProjection WithDamageEventCount(long value) =>
        new()
        {
            AnalyticsSemanticVersion = AnalyticsSemanticVersion.Current,
            Session = new CombatSessionSummary
            {
                Metrics = CombatSessionMetricSet.Empty with
                {
                    DamageEventCount = Metric<long>.Available(value)
                }
            }
        };

    private static AnalyticalProjectionView LegacyView(long totalDefeated)
    {
        var projection = LegacyObservationAdapter.ToProjection(new CharacterPerformanceObservation
        {
            SchemaVersion = 2,
            GameplaySessionId = GameplaySessionId.CreateNew(),
            SegmentOrdinal = 0,
            CharacterRecordId = CharacterRecordId.CreateNew(),
            StartedAtUtc = DateTimeOffset.UnixEpoch,
            EndedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1),
            TotalDefeated = totalDefeated
        });
        return new AnalyticalProjectionView
        {
            SourceKind = AnalyticalProjectionSourceKind.HistoricalLegacy,
            Projection = projection
        };
    }

    private static AnalyticalProjectionView DurableView(
        CombatAnalyticsProjection projection,
        int? semantic,
        int? attributionPolicy) =>
        new()
        {
            SourceKind = AnalyticalProjectionSourceKind.HistoricalDurable,
            Projection = projection,
            Header = new HistoricalSegmentHeader
            {
                SegmentId = Guid.NewGuid().ToString("N"),
                CaptureKind = HistoricalCaptureKind.DurableSegment,
                Compatibility = HistoricalCompatibility.AuthoritativeAggregate,
                GameplaySessionId = GameplaySessionId.CreateNew(),
                SegmentOrdinal = 0,
                CaptureStartUtc = DateTimeOffset.UnixEpoch,
                CaptureEndUtc = DateTimeOffset.UnixEpoch.AddMinutes(1),
                AnalyticsSemanticVersion = semantic,
                AttributionPolicyVersion = attributionPolicy
            }
        };

    private static CombatAnalyticsProjection WithDamage(Metric<CombatScaledAmount> damage, int semantic = 3) =>
        new()
        {
            AnalyticsSemanticVersion = semantic,
            Session = new CombatSessionSummary
            {
                DamageDealt = damage.Value.GetValueOrDefault(),
                Metrics = CombatSessionMetricSet.Empty with { DamageDealt = damage }
            }
        };

    private static CombatAnalyticsProjection WithAccuracy(long attempts, long hits, long misses) =>
        new()
        {
            AnalyticsSemanticVersion = AnalyticsSemanticVersion.Current,
            Session = new CombatSessionSummary
            {
                Accuracy = new CombatAccuracyScopeSnapshot { Attempts = attempts, Hits = hits, Misses = misses },
                Metrics = CombatSessionMetricSet.Empty with
                {
                    DamageDealt = Metric<CombatScaledAmount>.Available(new CombatScaledAmount(1)),
                    Accuracy = MetricRef<CombatAccuracyScopeSnapshot>.Available(
                        new CombatAccuracyScopeSnapshot { Attempts = attempts, Hits = hits, Misses = misses })
                }
            }
        };

    private static CombatAnalyticsProjection WithClockRate(long hundredths, RateDenominatorKind denominator) =>
        new()
        {
            AnalyticsSemanticVersion = AnalyticsSemanticVersion.Current,
            Session = new CombatSessionSummary
            {
                Metrics = CombatSessionMetricSet.Empty with
                {
                    DamageDealt = Metric<CombatScaledAmount>.Available(new CombatScaledAmount(1))
                }
            },
            Clock = new SegmentClock
            {
                WallClockDamagePerSecondHundredths = Metric<long>.Available(
                    hundredths,
                    MetricEvidence.DerivedFromObserved,
                    denominator: denominator)
            }
        };

    private static CombatAnalyticsProjection WithBuild(string manifest, string catalog) =>
        new()
        {
            AnalyticsSemanticVersion = AnalyticsSemanticVersion.Current,
            Session = new CombatSessionSummary
            {
                Metrics = CombatSessionMetricSet.Empty with
                {
                    DamageDealt = Metric<CombatScaledAmount>.Available(new CombatScaledAmount(10))
                }
            },
            BuildContext = new CombatBuildContextSummary
            {
                Availability = MetricAvailability.Available,
                Evidence = MetricEvidence.DerivedFromObserved,
                ManifestHash = manifest,
                BuildCatalogFingerprint = catalog
            }
        };

    private static CombatAnalyticsProjection WithAttribution(int policy, long unattributed, long proc) =>
        new()
        {
            AnalyticsSemanticVersion = AnalyticsSemanticVersion.Current,
            Session = new CombatSessionSummary
            {
                Metrics = CombatSessionMetricSet.Empty with
                {
                    DamageDealt = Metric<CombatScaledAmount>.Available(new CombatScaledAmount(unattributed + proc))
                }
            },
            Attribution = new CombatProcAttributionSummary
            {
                AttributionPolicyVersion = policy,
                ProcDamage = Metric<CombatScaledAmount>.Available(new CombatScaledAmount(proc)),
                UnattributedDamage = new CombatScaledAmount(unattributed),
                DirectDamage = CombatScaledAmount.Zero,
                UnattributedCount = unattributed > 0 ? 1 : 0
            }
        };

    private static CombatAnalyticsProjection Project(params string[] lines) => Apply(lines).Project();

    private static CombatEngine Apply(params string[] lines)
    {
        var engine = new CombatEngine();
        foreach (var canonical in ParseCanonical(lines))
        {
            engine.Apply(canonical);
        }

        return engine;
    }

    private static IReadOnlyList<CanonicalCombatEvent> ParseCanonical(params string[] lines)
    {
        var events = new List<CanonicalCombatEvent>(lines.Length);
        var contextId = MonitoringContextId.CreateNew();
        var segment = ParserSourceSegmentId.CreateNew();
        long sequence = 1;
        long byteStart = 0;
        foreach (var line in lines)
        {
            var logDate = line.StartsWith("2026-09-12 ", StringComparison.Ordinal)
                ? new DateOnly(2026, 9, 12)
                : line.StartsWith("2026-08-06 ", StringComparison.Ordinal)
                    ? new DateOnly(2026, 8, 6)
                    : new DateOnly(2026, 8, 4);
            var classified = CombatEventParserTestSupport.Classifier.Classify(new ParserRawEvent
            {
                ContextId = contextId,
                SourceId = LogSourceId.Create("acct-1", "acct-1", @"C:\fake\chatlog.txt", logDate),
                SourceSegmentId = segment,
                BindingGeneration = 1,
                SourceTransitionKind = MonitoringSourceTransitionKind.SourceAssigned,
                Sequence = sequence,
                ObservedAt = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero).AddSeconds(sequence - 1),
                RawLine = line,
                SourceByteStart = byteStart,
                SourceByteEnd = byteStart + line.Length + 2,
                LineStatus = ParserLineStatus.Complete
            });
            Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(classified, out var canonical));
            events.Add(canonical);
            byteStart = classified.SourceByteEnd;
            sequence++;
        }

        return events;
    }
}

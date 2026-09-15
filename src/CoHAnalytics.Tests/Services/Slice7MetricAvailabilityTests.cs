using System.Collections;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Replay;

namespace CoHAnalytics.Tests.Services;

public sealed class Slice7MetricAvailabilityTests
{
    private const string HotFeet =
        "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.";

    private const string IncomingDamage =
        "2026-08-04 12:00:00 Crey Thorn Mook hits you with Bone Shard for 22.15 points of Lethal damage.";

    private const string PetBrawl =
        "2026-09-12 05:38:47 Imp:  You hit Cleaner with your Brawl for 15.61 points of Fire damage over time.";

    private const string HastenActivated =
        "2026-09-12 05:25:01 You activated the Hasten power.";

    private const string HastenRecharged =
        "2026-09-12 05:25:08 Hasten is recharged.";

    private const string AttackResolution =
        "2026-08-06 12:00:00 HIT Rikti Pylon! Your Flashfire power had a 95.00% chance to hit, you rolled a 51.51.";

    [Fact]
    public void Observed_zero_damage_is_available_with_value_zero()
    {
        var canonical = Assert.Single(ParseCanonical(HotFeet));
        var engine = new CombatEngine();
        engine.Apply(canonical with { Amount = CombatScaledAmount.Zero });
        var metric = engine.Project().Session.Metrics.DamageDealt;
        Assert.Equal(MetricAvailability.Available, metric.Availability);
        Assert.Equal(CombatScaledAmount.Zero, metric.Value);
        Assert.Equal(MetricEvidence.DirectObserved, metric.Evidence);
        Assert.Equal(CombatScaledAmount.Zero, engine.Project().Session.DamageDealt);
    }

    [Fact]
    public void Unsupported_metrics_are_not_represented_as_zero()
    {
        var projection = Project(HotFeet);
        Assert.Equal(MetricAvailability.Unsupported, projection.Session.Metrics.Overkill.Availability);
        Assert.Null(projection.Session.Metrics.Overkill.Value);
        Assert.Equal(MetricAvailability.Unsupported, projection.Session.Metrics.TheoreticalRecharge.Availability);
        Assert.Null(projection.Session.Metrics.TheoreticalRecharge.Value);
        Assert.Equal(MetricAvailability.Unsupported, projection.Session.Metrics.PermaHasten.Availability);
        Assert.Equal(MetricAvailability.Unsupported, projection.Session.Metrics.MezDuration.Availability);
        Assert.Equal(MetricAvailability.Unsupported, projection.Session.Metrics.PetInstanceCount.Availability);
        Assert.NotEqual(0L, projection.Session.Metrics.Overkill.Value?.Hundredths ?? -1);
    }

    [Fact]
    public void NotCaptured_is_distinct_from_available_zero()
    {
        var empty = new CombatEngine().Project();
        Assert.Equal(MetricAvailability.NotCaptured, empty.Session.Metrics.DamageDealt.Availability);
        Assert.Null(empty.Session.Metrics.DamageDealt.Value);
        Assert.Equal(CombatScaledAmount.Zero, empty.Session.DamageDealt);

        var incoming = Project(IncomingDamage);
        Assert.Equal(MetricAvailability.NotCaptured, incoming.Session.Metrics.DamageDealt.Availability);
        Assert.Null(incoming.Session.Metrics.DamageDealt.Value);
        Assert.Equal(MetricAvailability.Available, incoming.Session.Metrics.DamageReceived.Availability);
        Assert.Equal(new CombatScaledAmount(2215), incoming.Session.Metrics.DamageReceived.Value);
    }

    [Fact]
    public void Pet_name_rollup_does_not_discard_observed_magnitude()
    {
        var projection = Project(PetBrawl);
        Assert.Equal(MetricAvailability.Available, projection.Session.Metrics.DamageDealtOwnedPets.Availability);
        Assert.Equal(new CombatScaledAmount(1561), projection.Session.Metrics.DamageDealtOwnedPets.Value);
        Assert.True(projection.Session.Metrics.DamageDealtOwnedPets.Coverage?.PetNameRollup);
        Assert.Equal(MetricEvidence.DirectObserved, projection.Session.Metrics.DamageDealtOwnedPets.Evidence);
    }

    [Fact]
    public void Direct_observed_damage_has_direct_evidence()
    {
        var metric = Project(HotFeet).Session.Metrics.DamageDealt;
        Assert.Equal(MetricAvailability.Available, metric.Availability);
        Assert.Equal(MetricEvidence.DirectObserved, metric.Evidence);
        Assert.Null(metric.Confidence);
        Assert.Equal(new CombatScaledAmount(1388), metric.Value);
    }

    [Fact]
    public void Derived_observed_span_uses_observed_at_not_source_precision()
    {
        var events = ParseCanonical(HotFeet, IncomingDamage);
        var engine = new CombatEngine();
        engine.Apply(events[0]);
        engine.Apply(events[1]);
        var span = engine.Project().Clock.ObservedAnalyticalSpan;
        Assert.Equal(MetricAvailability.Available, span.Availability);
        Assert.Equal(MetricEvidence.DerivedFromObserved, span.Evidence);
        Assert.Equal(TimeSpan.FromSeconds(1), span.Value);
    }

    [Fact]
    public void Null_damage_type_does_not_create_a_fake_available_type_row()
    {
        var canonical = Assert.Single(ParseCanonical(HotFeet));
        var engine = new CombatEngine();
        engine.Apply(canonical with { DamageType = null });
        var projection = engine.Project();
        Assert.Empty(projection.DamageTypes);
        Assert.Equal(MetricAvailability.Available, projection.Session.Metrics.DamageDealt.Availability);
    }

    [Fact]
    public void Target_overflow_marks_cardinality_as_coverage_limited_lower_bound()
    {
        var baseline = Assert.Single(ParseCanonical(HotFeet));
        var engine = new CombatEngine();
        for (var index = 0; index < CombatEngine.MaxTrackedTargets + 1; index++)
        {
            engine.Apply(baseline with
            {
                Sequence = index + 1,
                Target = ActorRef.UnknownNamed($"Target{index}")
            });
        }

        var metric = engine.Project().Session.Metrics.DistinctTargetCount;
        Assert.Equal(MetricAvailability.Incomplete, metric.Availability);
        Assert.True(metric.Coverage?.LowerBound);
        Assert.True(metric.Coverage?.Overflow);
        Assert.Equal(CombatEngine.MaxTrackedTargets, metric.Value);
        var power = Assert.Single(engine.Project().Powers, item => item.PowerName == "Hot Feet");
        Assert.Equal(MetricAvailability.Incomplete, power.DistinctTargetCountMetric.Availability);
    }

    [Fact]
    public void Accuracy_without_resolution_evidence_is_not_available()
    {
        var projection = Project(HotFeet);
        Assert.Equal(MetricAvailability.NotCaptured, projection.Session.Metrics.Accuracy.Availability);
        Assert.Null(projection.Session.Metrics.Accuracy.Value);
        Assert.Equal(0, projection.Session.Accuracy.Attempts);
    }

    [Fact]
    public void Confirmed_lifecycle_observation_is_derived_available()
    {
        var metric = Project(HastenActivated, HastenRecharged).Session.Metrics.ConfirmedRechargeCompletedCount;
        Assert.Equal(MetricAvailability.Available, metric.Availability);
        Assert.Equal(MetricEvidence.DerivedFromObserved, metric.Evidence);
        Assert.Equal(1, metric.Value);
    }

    [Fact]
    public void Unmatched_recharge_does_not_make_confirmed_recharge_available()
    {
        var projection = Project(HastenRecharged);
        Assert.Equal(MetricAvailability.NotCaptured, projection.Session.Metrics.ConfirmedRechargeCompletedCount.Availability);
        Assert.Null(projection.Session.Metrics.ConfirmedRechargeCompletedCount.Value);
        Assert.Equal(MetricAvailability.Available, projection.Session.Metrics.UnmatchedRechargeCandidateCount.Availability);
        Assert.Equal(1, projection.Session.Metrics.UnmatchedRechargeCandidateCount.Value);
        Assert.Equal(0, projection.Session.ConfirmedRechargeCompletedCount);
    }

    [Fact]
    public void Missing_activation_recharge_pair_does_not_create_an_observed_interval()
    {
        var projection = Project(HastenActivated);
        Assert.Equal(
            MetricAvailability.NotCaptured,
            projection.Session.Metrics.ObservedActivationToRechargeInterval.Availability);
        Assert.Null(projection.Session.Metrics.ObservedActivationToRechargeInterval.Value);
        Assert.Equal(
            MetricAvailability.NotCaptured,
            projection.Session.Metrics.ObservedRechargeToNextActivationInterval.Availability);
    }

    [Fact]
    public void Same_timestamp_events_preserve_parser_sequence()
    {
        var events = ParseCanonical(HotFeet, IncomingDamage);
        var engine = new CombatEngine();
        engine.Apply(events[0]);
        engine.Apply(events[1] with
        {
            ObservedAt = events[0].ObservedAt,
            SourceTimestamp = events[0].SourceTimestamp,
            Provenance = events[1].Provenance with { ObservedAt = events[0].ObservedAt, SourceTimestamp = events[0].SourceTimestamp }
        });
        var clock = engine.Project().Clock;
        Assert.Equal(events[0].Provenance.ParserSequence, clock.FirstParserSequence);
        Assert.Equal(events[1].Provenance.ParserSequence, clock.LastParserSequence);
        Assert.Equal(TimeSpan.Zero, clock.ObservedAnalyticalSpan.Value);
    }

    [Fact]
    public void Session_observed_span_uses_observed_at()
    {
        var projection = Project(HotFeet);
        Assert.Equal(projection.Clock.FirstObservedAt, projection.Clock.LastObservedAt);
        Assert.Equal(TimeSpan.Zero, projection.Clock.ObservedAnalyticalSpan.Value);
        Assert.Equal(MetricAvailability.NotCaptured, projection.Clock.WallClockDuration.Availability);
    }

    [Fact]
    public void Out_of_order_observed_at_does_not_create_negative_duration()
    {
        var events = ParseCanonical(HotFeet, IncomingDamage);
        var engine = new CombatEngine();
        engine.Apply(events[1]);
        engine.Apply(events[0]);
        var span = engine.Project().Clock.ObservedAnalyticalSpan.Value;
        Assert.NotNull(span);
        Assert.True(span >= TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(1), span);
    }

    [Fact]
    public void Session_boundary_resets_time_coverage()
    {
        var first = new CombatEngine();
        first.Apply(Assert.Single(ParseCanonical(HotFeet)));
        var second = new CombatEngine();
        Assert.Equal(MetricAvailability.NotCaptured, second.Project().Clock.ObservedAnalyticalSpan.Availability);
        Assert.Null(second.Project().Clock.FirstObservedAt);
        Assert.NotNull(first.Project().Clock.FirstObservedAt);
    }

    [Fact]
    public void Pet_coverage_limitation_and_instance_split_are_explicit()
    {
        var projection = Project(PetBrawl);
        var pet = Assert.Single(projection.Actors, item => item.Scope == CombatAnalyticsScope.PerPet);
        Assert.True(pet.CoverageLimited);
        Assert.Equal(MetricAvailability.Unsupported, pet.PetInstanceCount.Availability);
        Assert.Null(pet.PetInstanceCount.Value);
        Assert.Equal(MetricAvailability.Unsupported, projection.Session.Metrics.PetInstanceCount.Availability);
        Assert.Equal(MetricAvailability.Available, projection.Session.Metrics.DamageDealtOwnedPets.Availability);
    }

    [Fact]
    public void Slice6_session_scalars_remain_numerically_unchanged()
    {
        var projection = Project(HotFeet, IncomingDamage, PetBrawl);
        Assert.Equal(new CombatScaledAmount(1388 + 1561), projection.Session.DamageDealt);
        Assert.Equal(new CombatScaledAmount(1388), projection.Session.DamageDealtSelf);
        Assert.Equal(new CombatScaledAmount(1561), projection.Session.DamageDealtOwnedPets);
        Assert.Equal(new CombatScaledAmount(2215), projection.Session.DamageReceived);
        Assert.Equal(3, AnalyticsSemanticVersion.Current);
        Assert.Equal(3, projection.AnalyticsSemanticVersion);
    }

    [Fact]
    public void Projection_remains_read_only_and_default_availability_is_not_captured()
    {
        var projection = Project(HotFeet);
        Assert.Throws<NotSupportedException>(() => ((IList)projection.Powers).Add(null!));
        Assert.Equal(MetricAvailability.NotCaptured, default(Metric<long>).Availability);
        Assert.NotEqual(MetricAvailability.Available, default(Metric<long>).Availability);
    }

    [Fact]
    public void Capture_bounds_define_wall_clock_duration()
    {
        var start = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var engine = new CombatEngine();
        engine.Apply(Assert.Single(ParseCanonical(HotFeet)));
        var clock = engine.Project(new SegmentClockCapture
        {
            CaptureStartUtc = start,
            CaptureEndUtc = start.AddMinutes(2)
        }).Clock;
        Assert.Equal(MetricAvailability.Available, clock.WallClockDuration.Availability);
        Assert.Equal(TimeSpan.FromMinutes(2), clock.WallClockDuration.Value);
        Assert.Equal(RateDenominatorKind.WallClock, clock.WallClockDuration.Denominator);
        Assert.Equal(MetricEvidence.DerivedFromObserved, clock.WallClockDuration.Evidence);
        Assert.Equal(MetricAvailability.Available, clock.WallClockDamagePerSecondHundredths.Availability);
        Assert.Equal(RateDenominatorKind.WallClock, clock.WallClockDamagePerSecondHundredths.Denominator);
        Assert.Equal(
            CombatAggregator.CalculateDamagePerSecondHundredths(
                new CombatScaledAmount(1388),
                TimeSpan.FromMinutes(2)),
            clock.WallClockDamagePerSecondHundredths.Value);
        Assert.Equal(MetricEvidence.DerivedFromObserved, clock.WallClockDamagePerSecondHundredths.Evidence);
    }

    [Fact]
    public void Rate_metrics_are_not_captured_without_a_positive_duration()
    {
        var empty = new CombatEngine().Project(new SegmentClockCapture
        {
            CaptureStartUtc = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            CaptureEndUtc = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero)
        });
        Assert.Equal(MetricAvailability.Available, empty.Clock.WallClockDuration.Availability);
        Assert.Equal(TimeSpan.Zero, empty.Clock.WallClockDuration.Value);
        Assert.Equal(MetricAvailability.NotCaptured, empty.Clock.WallClockDamagePerSecondHundredths.Availability);
        Assert.Null(empty.Clock.WallClockDamagePerSecondHundredths.Value);
        Assert.Equal(MetricAvailability.Unsupported, empty.Clock.ActiveDamagePerSecondHundredths.Availability);
    }

    [Fact]
    public void Sub_millisecond_wall_clock_does_not_fabricate_a_rate()
    {
        var start = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var engine = new CombatEngine();
        engine.Apply(Assert.Single(ParseCanonical(HotFeet)));
        var clock = engine.Project(new SegmentClockCapture
        {
            CaptureStartUtc = start,
            CaptureEndUtc = start.AddTicks(1)
        }).Clock;
        Assert.Equal(MetricAvailability.Available, clock.WallClockDuration.Availability);
        Assert.True(clock.WallClockDuration.Value > TimeSpan.Zero);
        Assert.True(clock.WallClockDuration.Value < TimeSpan.FromMilliseconds(1));
        Assert.Equal(MetricAvailability.NotCaptured, clock.WallClockDamagePerSecondHundredths.Availability);
        Assert.Null(clock.WallClockDamagePerSecondHundredths.Value);
    }

    [Fact]
    public void Resolution_evidence_makes_accuracy_available()
    {
        var metric = Project(AttackResolution).Session.Metrics.Accuracy;
        Assert.Equal(MetricAvailability.Available, metric.Availability);
        Assert.Equal(MetricEvidence.DirectObserved, metric.Evidence);
        Assert.True(metric.Value?.HasAttempts);
    }

    [Fact]
    public async Task Legacy_wpf_snapshot_remains_player_only_scalars()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            using var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                new GameplaySessionOptions
                {
                    TimeProvider = time,
                    CombatSnapshotPublishInterval = TimeSpan.Zero
                });

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:00 Welcome to City of Heroes, Hero A!",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId && session.CharacterDisplayName == "Hero A"));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(HotFeet, contextId, source, sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.Combat.DamageDealt == new CombatScaledAmount(1388)));

            var session = Assert.Single(manager.Current.Sessions, item => item.ContextId == contextId);
            Assert.Equal(new CombatScaledAmount(1388), session.Combat.DamageDealt);
            Assert.Equal(session.Combat.DamageDealt, session.CombatAnalytics.Session.DamageDealtSelf);
            Assert.Equal(MetricAvailability.Available, session.CombatAnalytics.Session.Metrics.DamageDealt.Availability);
            Assert.Equal(MetricAvailability.Available, session.CombatAnalytics.Clock.WallClockDuration.Availability);
            Assert.Equal(3, session.CombatAnalytics.AnalyticsSemanticVersion);
            Assert.Null(session.CombatAnalytics.Clock.CaptureEndUtc);
            Assert.Equal(time.GetUtcNow(), session.CombatAnalytics.Clock.AsOfUtc);
            Assert.Equal(MetricAvailability.NotCaptured, session.CombatAnalytics.Clock.TrackedPauseAdjustedDuration.Availability);
            manager.StartTrackedCombat(contextId);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            Assert.Equal(TimeSpan.Zero, Assert.Single(manager.Current.Sessions).CombatAnalytics.Clock.TrackedPauseAdjustedDuration.Value);
            time.Advance(TimeSpan.FromSeconds(2));
            manager.PauseTrackedCombat(contextId);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            var paused = Assert.Single(manager.Current.Sessions);
            Assert.Equal(TimeSpan.FromSeconds(2), paused.CombatAnalytics.Clock.TrackedPauseAdjustedDuration.Value);
            time.Advance(TimeSpan.FromSeconds(3));
            manager.StopTrackedCombat(contextId);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            var stopped = Assert.Single(manager.Current.Sessions);
            Assert.False(stopped.Combat.Tracked.IsTracking);
            Assert.Equal(stopped.Combat.Tracked.ActiveElapsed, stopped.CombatAnalytics.Clock.TrackedPauseAdjustedDuration.Value);
            Assert.Equal(TimeSpan.FromSeconds(2), stopped.CombatAnalytics.Clock.TrackedPauseAdjustedDuration.Value);
            Assert.Equal(RateDenominatorKind.TrackedPauseAdjusted, stopped.CombatAnalytics.Clock.TrackedPauseAdjustedDuration.Denominator);
            await manager.StopAsync();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Metric_factories_reject_contradictions_and_defaults_are_safe()
    {
        Assert.Throws<ArgumentException>(() => Metric<long>.Available(0, MetricEvidence.None));
        Assert.Throws<ArgumentException>(() => Metric<long>.Available(0, MetricEvidence.CoverageLimited));
        Assert.Throws<ArgumentException>(() => Metric<long>.Available(1, coverage: new CoverageInfo { LowerBound = true }));
        Assert.Throws<ArgumentException>(() => Metric<long>.Incomplete(1, MetricEvidence.DirectObserved));
        Assert.Throws<ArgumentException>(() => Metric<long>.Incomplete(1, coverage: new CoverageInfo()));
        Assert.Throws<ArgumentException>(() => Metric<long>.Available(1, confidence: MetricConfidence.High));
        Assert.Throws<ArgumentException>(() => Metric<long>.Available(1, denominator: RateDenominatorKind.Unspecified));
        Assert.Throws<ArgumentNullException>(() => MetricRef<string>.Available(null!));
        Assert.Throws<ArgumentException>(() => MetricRef<string>.Available("value", MetricEvidence.None));
        Assert.Throws<ArgumentException>(() => MetricRef<string>.Available("value", coverage: new CoverageInfo { Overflow = true }));
        Assert.Null(default(Metric<long>).Value);
        Assert.Null(new MetricRef<string>().Value);
        Assert.Equal(MetricEvidence.None, Metric<long>.Unsupported().Evidence);
        Assert.Equal(MetricAvailability.NotCaptured, new MetricRef<string>().Availability);
        Assert.Equal(Metric<long>.Available(0), Metric<long>.Available(0));
        Assert.All(typeof(Metric<long>).GetProperties(), property => Assert.False(property.SetMethod?.IsPublic == true));
        Assert.All(typeof(MetricRef<string>).GetProperties(), property => Assert.False(property.SetMethod?.IsPublic == true));
        var partial = Metric<long>.Incomplete(4, coverage: new CoverageInfo { LowerBound = true });
        Assert.Equal(4, partial.Value);
        Assert.Equal(MetricEvidence.CoverageLimited, partial.Evidence);
        Assert.Equal(MetricAvailability.Incomplete, partial.Availability);
    }

    [Fact]
    public void Type_breakdown_is_partial_without_changing_exact_magnitude_or_other_direction()
    {
        var events = ParseCanonical(HotFeet, HotFeet, IncomingDamage);
        var engine = new CombatEngine();
        engine.Apply(events[0] with { Amount = new CombatScaledAmount(8000) });
        engine.Apply(events[1] with { Amount = new CombatScaledAmount(2000), DamageType = null });
        engine.Apply(events[2]);
        var result = engine.Project();
        Assert.Equal(new CombatScaledAmount(10000), result.Session.Metrics.DamageDealt.Value);
        Assert.Equal(MetricAvailability.Available, result.Session.Metrics.DamageDealt.Availability);
        Assert.Equal(MetricAvailability.Incomplete, result.DamageTypeBreakdown.Availability);
        Assert.True(result.DamageTypeBreakdown.Coverage?.MissingDamageType);
        Assert.Equal(new CombatScaledAmount(8000), Assert.Single(result.DamageTypeBreakdown.Value!).Amount);
        Assert.Equal(MetricAvailability.Available, result.IncomingDamageTypeBreakdown.Availability);
        Assert.Equal(MetricAvailability.Incomplete, Assert.Single(result.Powers, row => row.PowerName == "Hot Feet").DamageTypeBreakdown.Availability);
        Assert.Throws<NotSupportedException>(() => ((IList)result.DamageTypeBreakdown.Value!).Clear());
    }

    [Fact]
    public void Type_overflow_does_not_claim_target_overflow()
    {
        var item = Assert.Single(ParseCanonical(HotFeet));
        var engine = new CombatEngine();
        for (int i = 0; i <= CombatEngine.MaxTrackedDamageTypes; i++)
            engine.Apply(item with { DamageType = new DamageType($"Type{i}") });
        var result = engine.Project();
        var power = Assert.Single(result.Powers);
        Assert.True(power.CoverageLimited);
        Assert.Equal(MetricAvailability.Incomplete, power.DamageTypeBreakdown.Availability);
        Assert.Equal(MetricAvailability.Available, power.DistinctTargetCountMetric.Availability);
        Assert.Equal(1, power.DistinctTargetCountMetric.Value);
        Assert.Null(power.DistinctTargetCountMetric.Coverage);
        Assert.Equal(result.Session.DamageDealt.Hundredths, result.DamageTypes.Sum(row => row.Amount.Hundredths));
    }

    [Fact]
    public void Missing_target_is_lower_bound_without_overflow_or_magnitude_loss()
    {
        var item = Assert.Single(ParseCanonical(HotFeet));
        var engine = new CombatEngine();
        engine.Apply(item with { Target = ActorRef.Unknown });
        var result = engine.Project();
        Assert.Equal(item.Amount, result.Session.Metrics.DamageDealt.Value);
        Assert.Equal(MetricAvailability.Incomplete, result.Session.Metrics.DistinctTargetCount.Availability);
        Assert.True(result.Session.Metrics.DistinctTargetCount.Coverage?.MissingTarget);
        Assert.False(result.Session.Metrics.DistinctTargetCount.Coverage?.Overflow);
        Assert.Equal(0, result.Session.Metrics.DistinctTargetCount.Value);
        Assert.Equal(MetricAvailability.Incomplete, Assert.Single(result.Powers).DistinctTargetCountMetric.Availability);
    }

    [Fact]
    public void Mixed_units_are_unsupported_only_for_combined_magnitude()
    {
        var damage = Assert.Single(ParseCanonical(HotFeet));
        var endurance = Assert.Single(ParseCanonical("2026-09-12 05:24:59 You hit Hero_A with your Panacea: Chance for +Hit Points/Endurance granting them 7.5 points of endurance."));
        var engine = new CombatEngine();
        engine.Apply(damage);
        engine.Apply(endurance with { PowerName = damage.PowerName });
        var row = Assert.Single(engine.Project().Powers);
        Assert.Null(row.TotalMagnitude);
        Assert.Equal(MetricAvailability.Unsupported, row.TotalMagnitudeMetric.Availability);
        Assert.Null(row.TotalMagnitudeMetric.Value);
        Assert.Equal(damage.Amount, row.DamageMagnitudeMetric.Value);
        Assert.Equal(endurance.Amount, row.EnduranceMagnitudeMetric.Value);
        Assert.Equal(MetricAvailability.NotCaptured, row.HealingMagnitudeMetric.Availability);
        var activation = Assert.Single(Project(HastenActivated).Powers);
        Assert.Equal(MetricAvailability.NotCaptured, activation.TotalMagnitudeMetric.Availability);
    }

    [Fact]
    public void Invalid_capture_bounds_and_tracked_duration_are_not_observed_zero()
    {
        var start = DateTimeOffset.Parse("2026-08-04T12:00:00Z");
        var clock = new CombatEngine().Project(new SegmentClockCapture
        {
            CaptureStartUtc = start, CaptureEndUtc = start.AddSeconds(-1),
            TrackedPauseAdjustedDuration = TimeSpan.FromSeconds(-1)
        }).Clock;
        Assert.Equal(MetricAvailability.NotCaptured, clock.WallClockDuration.Availability);
        Assert.Null(clock.WallClockDuration.Value);
        Assert.Equal(MetricAvailability.NotCaptured, clock.TrackedPauseAdjustedDuration.Availability);
        Assert.Null(clock.WallClockDamagePerSecondHundredths.Value);
    }

    [Fact]
    public void Open_capture_is_as_of_and_final_capture_is_stable()
    {
        var start = DateTimeOffset.Parse("2026-08-04T12:00:00Z");
        var engine = new CombatEngine();
        var capture = new SegmentClockCapture { CaptureStartUtc = start, AsOfUtc = start.AddSeconds(2) };
        var open = engine.Project(capture).Clock;
        Assert.Null(open.CaptureEndUtc);
        Assert.Equal(capture.AsOfUtc, open.AsOfUtc);
        Assert.Equal(TimeSpan.FromSeconds(2), open.WallClockDuration.Value);
        var final = capture with { CaptureEndUtc = start.AddSeconds(3) };
        Assert.Equal(engine.Project(final).Clock, engine.Project(final with { AsOfUtc = start.AddHours(1) }).Clock);
        Assert.Equal(final.CaptureEndUtc, engine.Project(final).Clock.AsOfUtc);
    }

    [Fact]
    public void Clock_endpoints_keep_their_source_scope_and_replay_deterministically()
    {
        var events = ParseCanonical(HotFeet, IncomingDamage);
        var second = events[1] with { Provenance = events[1].Provenance with
            { ParserSequence = 1, SourceSegmentId = Guid.NewGuid() } };
        var engine = new CombatEngine();
        engine.Apply(events[0]); engine.Apply(second);
        var clock = engine.Project().Clock;
        Assert.Equal(second.Provenance, clock.LastObservation);
        Assert.Equal(second.SourceTimestamp, clock.LastSourceTimestamp);
        Assert.Equal(second.ObservedAt, clock.LastObservedAt);
        Assert.Equal(1, clock.LastParserSequence);
        var replay = new CombatEngine();
        replay.Apply(events[0]); replay.Apply(second);
        Assert.Equal(clock, replay.Project().Clock);
        var reverse = new CombatEngine();
        reverse.Apply(second); reverse.Apply(events[0]);
        Assert.Equal(clock, reverse.Project().Clock);
    }

    [Fact]
    public void Active_duration_is_not_fabricated_from_ingestion_gaps()
    {
        var engine = new CombatEngine();
        foreach (var item in ParseCanonical(HotFeet, HastenActivated, HotFeet)) engine.Apply(item);
        Assert.Equal(MetricAvailability.Unsupported, engine.Project().Clock.ActiveDuration.Availability);
        Assert.Null(engine.Project().Clock.ActiveDuration.Value);
        Assert.Equal(MetricAvailability.Unsupported, engine.Project().Clock.ActiveDamagePerSecondHundredths.Availability);
    }

    [Theory]
    [InlineData(0, 10000000, 0)]
    [InlineData(1388, 15000, 925333)]
    [InlineData(long.MaxValue, 10000000, long.MaxValue)]
    public void Capture_wall_rate_uses_tick_precision_and_safe_intermediate_arithmetic(long amount, long ticks, long expected)
    {
        var engine = new CombatEngine();
        engine.Apply(Assert.Single(ParseCanonical(HotFeet)) with { Amount = new CombatScaledAmount(amount) });
        var start = DateTimeOffset.Parse("2026-08-04T12:00:00Z");
        var rate = engine.Project(new SegmentClockCapture { CaptureStartUtc = start, CaptureEndUtc = start.AddTicks(ticks) }).Clock.WallClockDamagePerSecondHundredths;
        Assert.Equal(MetricAvailability.Available, rate.Availability);
        Assert.Equal(expected, rate.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10000)]
    public void Nonzero_damage_without_representable_rate_is_not_available(long ticks)
    {
        var engine = new CombatEngine();
        engine.Apply(Assert.Single(ParseCanonical(HotFeet)) with { Amount = new CombatScaledAmount(long.MaxValue) });
        var start = DateTimeOffset.Parse("2026-08-04T12:00:00Z");
        var rate = engine.Project(new SegmentClockCapture { CaptureStartUtc = start, CaptureEndUtc = start.AddTicks(ticks) }).Clock.WallClockDamagePerSecondHundredths;
        Assert.Equal(MetricAvailability.NotCaptured, rate.Availability);
        Assert.Null(rate.Value);
    }

    [Fact]
    public void Legacy_submillisecond_guard_does_not_change_valid_millisecond_results()
    {
        Assert.Equal(0, CombatAggregator.CalculateDamagePerSecondHundredths(new CombatScaledAmount(100), TimeSpan.FromTicks(1)));
        Assert.Equal(100, CombatAggregator.CalculateDamagePerSecondHundredths(new CombatScaledAmount(100), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Activation_without_recharge_evidence_does_not_infer_zero_completions()
    {
        var projection = Project(HastenActivated);
        Assert.Equal(MetricEvidence.DirectObserved, projection.Session.Metrics.ActivationCount.Evidence);
        Assert.Equal(1, projection.Session.Metrics.ActivationCount.Value);
        Assert.Equal(MetricAvailability.NotCaptured, projection.Session.Metrics.ConfirmedRechargeCompletedCount.Availability);
        var confirmed = Project(HastenActivated, HastenRecharged.Replace("is recharged", "is still recharging"));
        Assert.Equal(MetricEvidence.DerivedFromObserved, confirmed.Session.Metrics.ConfirmedStillRechargingCount.Evidence);
        Assert.Equal(1, confirmed.Session.Metrics.ConfirmedStillRechargingCount.Value);
        Assert.Null(confirmed.Session.Metrics.ObservedActivationToRechargeInterval.Value);
    }

    [Fact]
    public void Autohit_is_observed_without_fabricating_accuracy_attempts()
    {
        var result = Project("2026-08-06 12:00:05 HIT Training Dummy! Your Siphon Power power is autohit.");
        var metric = result.Session.Metrics.Accuracy;
        Assert.Equal(MetricAvailability.Available, metric.Availability);
        Assert.Equal(1, metric.Value!.Autohits);
        Assert.False(metric.Value.HasAttempts);
        Assert.Equal(result.Session.Accuracy, metric.Value);
        var incoming = Project("2026-09-12 05:24:59 Hero_A HITS you! Health power was autohit.");
        Assert.Equal(MetricAvailability.NotCaptured, incoming.Session.Metrics.Accuracy.Availability);
        Assert.Equal(MetricAvailability.Unsupported, result.Session.Metrics.CompanionMissResolutionCount.Availability);
    }

    [Theory]
    [InlineData("Hero B")]
    [InlineData("Hero A")]
    public async Task Live_true_Welcome_boundaries_reset_clock_and_metric_evidence(string nextName)
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var context = MonitoringContextId.CreateNew();
        var source = GameplaySessionTestInfrastructure.DefaultSource();
        var segment = ParserSourceSegmentId.CreateNew();
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-08-04T12:00:00Z"));
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, GameplaySessionTestInfrastructure.ReadyContext(context, source)));
        using var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(monitoring, parser, repository,
            new GameplaySessionOptions { TimeProvider = time, CombatSnapshotPublishInterval = TimeSpan.Zero });
        try
        {
            long sequence = 0;
            async Task Send(string text)
            {
                var seq = ++sequence;
                parser.PublishClassified([GameplaySessionTestInfrastructure.Classify("2026-08-04 12:00:00 " + text, context, source, seq) with
                { SourceSegmentId = segment, SourceByteStart = seq * 300, SourceByteEnd = seq * 300 + 200, ObservedAt = time.GetUtcNow() }]);
                await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            }
            await Send("Welcome to City of Heroes, Hero A!");
            await Send("You activated the Hasten power.");
            var initial = Assert.Single(manager.Current.Sessions);
            var initialClock = initial.CombatAnalytics.Clock;
            foreach (var name in new[] { nextName, "Hero A" })
            {
                time.Advance(TimeSpan.FromSeconds(5));
                await Send($"Welcome to City of Heroes, {name}!");
                var fresh = Assert.Single(manager.Current.Sessions);
                Assert.NotEqual(initial.SessionId, fresh.SessionId);
                Assert.Equal(time.GetUtcNow(), fresh.CombatAnalytics.Clock.CaptureStartUtc);
                Assert.Null(fresh.CombatAnalytics.Clock.FirstObservation);
                Assert.Equal(MetricAvailability.NotCaptured, fresh.CombatAnalytics.Session.Metrics.ActivationCount.Availability);
                await Send("Hasten is recharged.");
                var observed = Assert.Single(manager.Current.Sessions);
                Assert.Equal(time.GetUtcNow(), observed.CombatAnalytics.Clock.FirstObservedAt);
                Assert.Equal(MetricAvailability.NotCaptured, observed.CombatAnalytics.Session.Metrics.ConfirmedRechargeCompletedCount.Availability);
                Assert.Null(observed.CombatAnalytics.Clock.CaptureEndUtc);
            }
            Assert.Equal(initialClock, initial.CombatAnalytics.Clock);
            await manager.StopAsync();
            Assert.Empty(manager.Current.Sessions);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static CombatAnalyticsProjection Project(params string[] lines)
    {
        var engine = new CombatEngine();
        foreach (var canonical in ParseCanonical(lines))
        {
            engine.Apply(canonical);
        }

        return engine.Project();
    }

    private static IReadOnlyList<CanonicalCombatEvent> ParseCanonical(params string[] lines)
    {
        var segment = ParserSourceSegmentId.CreateNew();
        var contextId = MonitoringContextId.CreateNew();
        var events = new List<CanonicalCombatEvent>(lines.Length);
        long sequence = 1;
        long byteStart = 0;
        foreach (var line in lines)
        {
            var classified = Classify(line, contextId, segment, sequence, byteStart);
            Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(classified, out var canonical));
            events.Add(canonical);
            byteStart = classified.SourceByteEnd;
            sequence++;
        }

        return events;
    }

    private static ParserEvent Classify(
        string line,
        MonitoringContextId contextId,
        ParserSourceSegmentId segmentId,
        long sequence,
        long byteStart)
    {
        var logDate = line.StartsWith("2026-09-12 ", StringComparison.Ordinal)
            ? new DateOnly(2026, 9, 12)
            : line.StartsWith("2026-08-06 ", StringComparison.Ordinal)
                ? new DateOnly(2026, 8, 6)
                : new DateOnly(2026, 8, 4);
        return CombatEventParserTestSupport.Classifier.Classify(new ParserRawEvent
        {
            ContextId = contextId,
            SourceId = LogSourceId.Create(
                "acct-1",
                "acct-1",
                "C:\\fake\\chatlog.txt",
                logDate),
            SourceSegmentId = segmentId,
            BindingGeneration = 1,
            SourceTransitionKind = MonitoringSourceTransitionKind.SourceAssigned,
            Sequence = sequence,
            ObservedAt = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero).AddSeconds(sequence - 1),
            RawLine = line,
            SourceByteStart = byteStart,
            SourceByteEnd = byteStart + line.Length + 2,
            LineStatus = ParserLineStatus.Complete
        });
    }
}

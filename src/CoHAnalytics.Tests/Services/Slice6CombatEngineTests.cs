using System.Collections;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Slice 6 dimensioned CombatEngine projections. Live WPF scalars remain on CombatAggregator.
/// </summary>
public sealed class Slice6CombatEngineTests
{
    private const string HotFeet =
        "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.";

    private const string HotFeetLarger =
        "2026-09-12 05:38:44 You hit Dismantler with your Hot Feet for 19.71 points of Fire damage.";

    private const string FireCagesTick =
        "2026-09-12 05:36:30 You hit Cleaner with your Fire Cages for 2.57 points of Fire damage over time.";

    private const string IncomingDamage =
        "2026-08-04 12:00:00 Crey Thorn Mook hits you with Bone Shard for 22.15 points of Lethal damage.";

    private const string HealDealt =
        "2026-09-12 05:36:59 You heal Hero_A with Transfusion for 421.34 health points.";

    private const string HealReceived =
        "2026-09-12 05:36:59 Hero_A heals you with their Transfusion for 421.34 health points.";

    private const string EnduranceDealt =
        "2026-09-12 05:24:59 You hit Hero_A with your Panacea: Chance for +Hit Points/Endurance granting them 7.5 points of endurance.";

    private const string EnduranceReceived =
        "2026-09-12 05:24:59 Hero_A hits you with their Panacea: Chance for +Hit Points/Endurance granting you 7.5 points of endurance.";

    private const string PlayerBrawl =
        "2026-09-12 05:38:43 You hit Cleaner with your Brawl for 10.00 points of Fire damage.";

    private const string PetBrawl =
        "2026-09-12 05:38:47 Imp:  You hit Cleaner with your Brawl for 15.61 points of Fire damage over time.";

    private const string HastenActivated =
        "2026-09-12 05:25:01 You activated the Hasten power.";

    private const string HastenRecharged =
        "2026-09-12 05:25:08 Hasten is recharged.";

    private const string FireCagesStillRecharging =
        "2026-09-12 05:36:20 Fire Cages is still recharging.";

    private const string FireCagesActivated =
        "2026-08-04 12:00:00 You activated the Fire Cages power.";

    private const string AttackResolution =
        "2026-08-06 12:00:00 HIT Rikti Pylon! Your Flashfire power had a 95.00% chance to hit, you rolled a 51.51.";

    private const string Defeat =
        "2026-08-04 12:00:00 You have defeated Lusca";

    [Fact]
    public void Single_outgoing_damage_updates_session_total()
    {
        var projection = Project(HotFeet);
        Assert.Equal(new CombatScaledAmount(1388), projection.Session.DamageDealt);
        Assert.Equal(new CombatScaledAmount(1388), projection.Session.DamageDealtSelf);
        Assert.Equal(CombatScaledAmount.Zero, projection.Session.DamageDealtOwnedPets);
        Assert.Equal(CombatScaledAmount.Zero, projection.Session.DamageReceived);
        Assert.Equal(1, projection.Session.DamageEventCount);
        Assert.Equal(1, projection.LogicalEventsApplied);
    }

    [Fact]
    public void Incoming_damage_updates_incoming_only()
    {
        var projection = Project(IncomingDamage);
        Assert.Equal(CombatScaledAmount.Zero, projection.Session.DamageDealt);
        Assert.Equal(new CombatScaledAmount(2215), projection.Session.DamageReceived);
        Assert.Equal(1, projection.Session.DamageEventCount);
    }

    [Fact]
    public void Heal_dealt_and_received_remain_directional()
    {
        var projection = Project(HealDealt, HealReceived);
        Assert.Equal(new CombatScaledAmount(42134), projection.Session.HealingDealt);
        Assert.Equal(new CombatScaledAmount(42134), projection.Session.HealingReceived);
        Assert.Equal(new CombatScaledAmount(42134), projection.Session.HealingDealtSelf);
        Assert.Equal(2, projection.Session.HealEventCount);
        Assert.Equal(CombatScaledAmount.Zero, projection.Session.DamageDealt);
    }

    [Fact]
    public void Endurance_dealt_and_received_remain_directional()
    {
        var projection = Project(EnduranceDealt, EnduranceReceived);
        Assert.Equal(new CombatScaledAmount(750), projection.Session.EnduranceGranted);
        Assert.Equal(new CombatScaledAmount(750), projection.Session.EnduranceReceived);
        Assert.Equal(2, projection.Session.EnduranceEventCount);
        Assert.Equal(CombatScaledAmount.Zero, projection.Session.DamageDealt);
        Assert.Equal(CombatScaledAmount.Zero, projection.Session.HealingDealt);
    }

    [Fact]
    public void Two_logical_events_from_same_power_aggregate()
    {
        var projection = Project(HotFeet, HotFeetLarger);
        var row = Assert.Single(projection.Powers, item => item.Scope == CombatAnalyticsScope.Self);
        Assert.Equal("Hot Feet", row.PowerName);
        Assert.Equal(new CombatScaledAmount(1388 + 1971), row.TotalMagnitude);
        Assert.Equal(2, row.EventCount);
        Assert.Equal(new CombatScaledAmount(1971), row.LargestHit);
    }

    [Fact]
    public void Dedup_duplicate_occurrence_does_not_double_magnitude()
    {
        var events = ParseCanonical(HotFeet, HotFeet);
        var duplicate = events[1] with
        {
            DuplicateOf = new EventOccurrenceRef
            {
                SourceId = events[0].Provenance.SourceId,
                SourceSegmentId = events[0].Provenance.SourceSegmentId,
                BindingGeneration = events[0].Provenance.BindingGeneration,
                ParserSequence = events[0].Provenance.ParserSequence
            }
        };

        var engine = new CombatEngine();
        engine.Apply(events[0]);
        engine.Apply(duplicate);
        var projection = engine.Project();
        Assert.Equal(new CombatScaledAmount(1388), projection.Session.DamageDealt);
        Assert.Equal(1, projection.LogicalEventsApplied);
        Assert.Equal(1, projection.DuplicateOccurrencesIgnored);
    }

    [Fact]
    public void Different_powers_remain_separate()
    {
        var projection = Project(HotFeet, FireCagesTick);
        Assert.Equal(2, projection.Powers.Count(item => item.Scope == CombatAnalyticsScope.Self));
        Assert.Contains(projection.Powers, item => item.PowerName == "Hot Feet");
        Assert.Contains(projection.Powers, item => item.PowerName == "Fire Cages");
    }

    [Fact]
    public void Same_power_name_from_player_and_owned_pet_stays_dimensionally_distinct()
    {
        var projection = Project(PlayerBrawl, PetBrawl);
        var self = Assert.Single(
            projection.Powers,
            item => item.Scope == CombatAnalyticsScope.Self && item.PowerName == "Brawl");
        var pet = Assert.Single(
            projection.Powers,
            item => item.Scope == CombatAnalyticsScope.PerPet && item.PowerName == "Brawl");
        Assert.Equal(new CombatScaledAmount(1000), self.TotalMagnitude);
        Assert.Equal(new CombatScaledAmount(1561), pet.TotalMagnitude);
        Assert.Equal("Imp", pet.PetNormalizedName);
        Assert.Null(self.PetNormalizedName);
    }

    [Fact]
    public void Pet_contribution_appears_in_combined_session_total()
    {
        var projection = Project(HotFeet, PetBrawl);
        Assert.Equal(new CombatScaledAmount(1388), projection.Session.DamageDealtSelf);
        Assert.Equal(new CombatScaledAmount(1561), projection.Session.DamageDealtOwnedPets);
        Assert.Equal(new CombatScaledAmount(1388 + 1561), projection.Session.DamageDealt);
        var pets = Assert.Single(projection.Actors, item => item.Scope == CombatAnalyticsScope.OwnPetsAggregate);
        Assert.Equal(new CombatScaledAmount(1561), pets.DamageDealt);
    }

    [Fact]
    public void Damage_type_breakdown_is_correct()
    {
        var projection = Project(HotFeet, IncomingDamage);
        var fire = Assert.Single(projection.DamageTypes);
        Assert.Equal("Fire", fire.DamageType.Text);
        Assert.Equal(new CombatScaledAmount(1388), fire.Amount);
        Assert.DoesNotContain(projection.DamageTypes, item => item.DamageType.Text == "Lethal");
    }

    [Fact]
    public void Direct_versus_dot_split_uses_canonical_delivery()
    {
        var projection = Project(HotFeet, FireCagesTick);
        var hotFeet = Assert.Single(projection.Powers, item => item.PowerName == "Hot Feet");
        var cages = Assert.Single(projection.Powers, item => item.PowerName == "Fire Cages");
        Assert.Equal(new CombatScaledAmount(1388), hotFeet.DirectAmount);
        Assert.Equal(CombatScaledAmount.Zero, hotFeet.DotAmount);
        Assert.Equal(CombatScaledAmount.Zero, cages.DirectAmount);
        Assert.Equal(new CombatScaledAmount(257), cages.DotAmount);
    }

    [Fact]
    public void Largest_hit_is_the_observed_magnitude()
    {
        var projection = Project(FireCagesTick, HotFeetLarger, HotFeet);
        var hotFeet = Assert.Single(projection.Powers, item => item.PowerName == "Hot Feet");
        Assert.Equal(new CombatScaledAmount(1971), hotFeet.LargestHit);
    }

    [Fact]
    public void Distinct_target_tracking_is_correct()
    {
        var projection = Project(HotFeet, HotFeetLarger);
        Assert.Equal(2, projection.Targets.Count);
        Assert.Contains(projection.Targets, item => item.NormalizedTargetName == "Lusca");
        Assert.Contains(projection.Targets, item => item.NormalizedTargetName == "Dismantler");
        var hotFeet = Assert.Single(projection.Powers, item => item.PowerName == "Hot Feet");
        Assert.Equal(2, hotFeet.DistinctTargetCount);
    }

    [Fact]
    public void Unknown_target_does_not_fabricate_a_target_bucket()
    {
        var canonical = Assert.Single(ParseCanonical(HotFeet));
        var engine = new CombatEngine();
        engine.Apply(canonical with { Target = null });
        engine.Apply(canonical with
        {
            Sequence = 2,
            Target = new ActorRef { Type = ActorType.Environment, DisplayName = "Poison Gas" }
        });
        var projection = engine.Project();
        Assert.Empty(projection.Targets);
        Assert.Equal(new CombatScaledAmount(1388 * 2), projection.Session.DamageDealt);
    }

    [Fact]
    public void Activation_count_is_correct()
    {
        var projection = Project(HastenActivated, HastenActivated, FireCagesTick);
        Assert.Equal(2, projection.Session.ActivationCount);
        var hasten = Assert.Single(projection.Powers, item => item.PowerName == "Hasten");
        Assert.Equal(2, hasten.ActivationCount);
        Assert.Equal(0, hasten.EventCount);
        Assert.Null(hasten.LargestHit);
    }

    [Fact]
    public void Recharge_candidate_without_prior_activation_is_not_confirmed()
    {
        var projection = Project(HastenRecharged);
        Assert.Equal(0, projection.Session.ConfirmedRechargeCompletedCount);
        Assert.Equal(1, projection.Session.UnmatchedRechargeCandidateCount);
        Assert.DoesNotContain(projection.Powers, item => item.PowerName == "Hasten");
        Assert.Equal(CombatScaledAmount.Zero, projection.Session.DamageDealt);
    }

    [Fact]
    public void Same_name_recharge_from_another_session_does_not_confirm()
    {
        var events = ParseCanonical(HastenActivated, HastenRecharged);
        var firstSession = new CombatEngine();
        firstSession.Apply(events[0]);
        var secondSession = new CombatEngine();
        secondSession.Apply(events[1]);
        var projection = secondSession.Project();
        Assert.Equal(0, projection.Session.ConfirmedRechargeCompletedCount);
        Assert.Equal(1, projection.Session.UnmatchedRechargeCandidateCount);
        Assert.Equal(1, firstSession.Project().Session.ActivationCount);
    }

    [Fact]
    public void Same_session_activation_confirms_matching_recharge_observation()
    {
        var projection = Project(HastenActivated, HastenRecharged);
        Assert.Equal(1, projection.Session.ActivationCount);
        Assert.Equal(1, projection.Session.ConfirmedRechargeCompletedCount);
        Assert.Equal(0, projection.Session.UnmatchedRechargeCandidateCount);
        var hasten = Assert.Single(projection.Powers, item => item.PowerName == "Hasten");
        Assert.Equal(1, hasten.ConfirmedRechargeCompletedCount);
        Assert.Equal(CombatScaledAmount.Zero, projection.Session.DamageDealt);
        Assert.Equal(CombatScaledAmount.Zero, hasten.TotalMagnitude);
    }

    [Fact]
    public void Repeated_still_recharging_observations_remain_distinct()
    {
        var projection = Project(
            FireCagesActivated,
            FireCagesStillRecharging,
            FireCagesStillRecharging);
        Assert.Equal(2, projection.Session.ConfirmedStillRechargingCount);
        var cages = Assert.Single(projection.Powers, item => item.PowerName == "Fire Cages");
        Assert.Equal(2, cages.ConfirmedStillRechargingCount);
    }

    [Fact]
    public void Lifecycle_events_do_not_contribute_magnitude()
    {
        var projection = Project(HastenActivated, HastenRecharged, FireCagesStillRecharging, AttackResolution);
        Assert.Equal(CombatScaledAmount.Zero, projection.Session.DamageDealt);
        Assert.Equal(CombatScaledAmount.Zero, projection.Session.HealingDealt);
        Assert.Equal(CombatScaledAmount.Zero, projection.Session.EnduranceGranted);
        Assert.Equal(0, projection.Session.DamageEventCount);
        Assert.Equal(1, projection.Session.AttackResolutionCount);
    }

    [Fact]
    public void LogicalEvents_contract_is_respected_for_collapsed_heal_mirror()
    {
        var events = WithComplementaryHealChannels(ParseCanonical(HealReceived, HealDealt));
        var result = new Deduplicator(MirrorCompatibilityPolicy.ForTests(TestHealRule())).Deduplicate(events);
        Assert.Single(result.LogicalEvents);

        var engine = new CombatEngine();
        foreach (var item in result.Events)
        {
            engine.Apply(item);
        }

        var projection = engine.Project();
        Assert.Equal(1, projection.LogicalEventsApplied);
        Assert.Equal(1, projection.DuplicateOccurrencesIgnored);
        Assert.Equal(new CombatScaledAmount(42134), projection.Session.HealingDealt);
        Assert.Equal(new CombatScaledAmount(42134), projection.Session.HealingReceived);
        Assert.Equal(1, projection.Session.HealEventCount);
    }

    [Fact]
    public void Projection_snapshot_is_read_only_from_consumer_perspective()
    {
        var projection = Project(HotFeet);
        Assert.Equal(AnalyticsSemanticVersion.Current, projection.AnalyticsSemanticVersion);
        Assert.Equal(2, AnalyticsSemanticVersion.Current);
        Assert.Throws<NotSupportedException>(() => ((IList)projection.Powers).Add(null!));
        Assert.Throws<NotSupportedException>(() => ((IList)projection.DamageTypes).Add(null!));
        Assert.Throws<NotSupportedException>(() => ((IList)projection.Actors).Add(null!));
        Assert.Throws<NotSupportedException>(() => ((IList)projection.Targets).Add(null!));
        var first = projection.Session.DamageDealt;
        _ = projection with { LogicalEventsApplied = 99 };
        Assert.Equal(first, Project(HotFeet).Session.DamageDealt);
    }

    [Fact]
    public void Target_cardinality_overflow_uses_other_bucket()
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

        var projection = engine.Project();
        Assert.True(projection.CoverageLimited);
        Assert.Contains(
            projection.Targets,
            item => item.IsOverflow && item.NormalizedTargetName == CombatEngine.OverflowBucketKey);
        Assert.Equal(CombatEngine.MaxTrackedTargets + 1, projection.Targets.Count);
    }

    [Fact]
    public void Pet_grammar_behavior_remains_unchanged()
    {
        var input = CombatEventParserTestSupport.Classify(PetBrawl);
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(input, out var canonical));
        Assert.Equal(ActorType.OwnPet, canonical.Actor.Type);
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out _));
        Assert.Equal(4, EventProvenance.CurrentGrammarSetVersion);
    }

    [Fact]
    public void Dedup_policy_remains_unchanged()
    {
        Assert.Equal(1, DedupPolicyVersion.Current);
        Assert.Empty(MirrorCompatibilityPolicy.Version1.EnabledRules);
        Assert.Equal(4, EventProvenance.CurrentGrammarSetVersion);
        Assert.NotEqual(EventProvenance.CurrentGrammarSetVersion, DedupPolicyVersion.Current);
    }

    [Fact]
    public void Player_only_engine_scalars_match_legacy_aggregator()
    {
        var lines = new[] { HotFeet, IncomingDamage, HealDealt, HealReceived, AttackResolution, Defeat, HastenActivated };
        var aggregator = new CombatAggregator();
        var engine = new CombatEngine();
        foreach (var canonical in ParseCanonical(lines))
        {
            engine.Apply(canonical);
            if (CombatEventParserTestSupport.Parser.TryAdaptToLegacy(canonical, out var legacy))
            {
                aggregator.Apply(legacy);
            }
        }

        var snapshot = aggregator.ToSnapshot(
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 4, 12, 1, 0, TimeSpan.Zero),
            timingEndAt: null,
            idleThreshold: TimeSpan.FromSeconds(10));
        var projection = engine.Project();
        Assert.Equal(snapshot.DamageDealt, projection.Session.DamageDealtSelf);
        Assert.Equal(snapshot.DamageReceived, projection.Session.DamageReceived);
        Assert.Equal(snapshot.HealingDealt, projection.Session.HealingDealtSelf);
        Assert.Equal(snapshot.HealingReceived, projection.Session.HealingReceived);
        Assert.Equal(snapshot.PowerActivations, projection.Session.ActivationCount);
        Assert.Equal(snapshot.TotalDefeated, projection.Session.DefeatCount);
        Assert.Equal(snapshot.MyDefeats, projection.Session.MyDefeatCount);
        Assert.Equal(snapshot.Accuracy, projection.Session.Accuracy);
    }

    [Fact]
    public void Healing_is_not_direct_damage_dot_or_largest_hit()
    {
        var row = Assert.Single(Project(HealDealt).Powers);
        Assert.Equal(CombatScaledAmount.Zero, row.DirectAmount);
        Assert.Equal(CombatScaledAmount.Zero, row.DotAmount);
        Assert.Null(row.LargestHit);
        Assert.Equal(new CombatScaledAmount(42134), row.HealingMagnitude);
    }

    [Fact]
    public void Mixed_hp_and_endurance_do_not_fabricate_a_combined_unit()
    {
        var events = ParseCanonical(HealDealt, EnduranceDealt);
        var engine = new CombatEngine();
        engine.Apply(events[0]);
        engine.Apply(events[1] with { PowerName = events[0].PowerName });
        var power = Assert.Single(engine.Project().Powers);
        Assert.Null(power.TotalMagnitude);
        Assert.Null(power.TotalMagnitudeKind);
        Assert.Equal(new CombatScaledAmount(42134), power.HealingMagnitude);
        Assert.Equal(new CombatScaledAmount(750), power.EnduranceMagnitude);
    }

    [Fact]
    public void Incoming_power_and_type_dimensions_are_separate_from_outgoing()
    {
        var projection = Project(HotFeet, IncomingDamage);
        var incoming = Assert.Single(projection.Powers, item => item.Direction == CombatAnalyticsDirection.Incoming);
        Assert.Equal("Bone Shard", incoming.PowerName);
        Assert.Equal(new CombatScaledAmount(2215), incoming.DamageMagnitude);
        Assert.Equal(incoming.DamageMagnitude, incoming.DirectAmount + incoming.DotAmount);
        Assert.Equal("Lethal", Assert.Single(incoming.DamageTypes).DamageType.Text);
        Assert.Equal("Lethal", Assert.Single(projection.IncomingDamageTypes).DamageType.Text);
        Assert.Equal("Fire", Assert.Single(projection.DamageTypes).DamageType.Text);
    }

    [Fact]
    public void Mirror_union_updates_self_directional_views_regardless_of_survivor_family()
    {
        foreach (var pair in new[] { new[] { HealReceived, HealDealt }, new[] { HealDealt, HealReceived } })
        {
            var result = new Deduplicator(MirrorCompatibilityPolicy.ForTests(TestHealRule()))
                .Deduplicate(WithComplementaryHealChannels(ParseCanonical(pair)));
            var engine = new CombatEngine();
            foreach (var item in result.LogicalEvents) engine.Apply(item);
            var projection = engine.Project();
            var self = Assert.Single(projection.Actors, item => item.Scope == CombatAnalyticsScope.Self);
            Assert.Equal(new CombatScaledAmount(42134), self.HealingDealt);
            Assert.Equal(self.HealingDealt, self.HealingReceived);
            Assert.Equal(self.HealingDealt, projection.Session.HealingDealtSelf);
            Assert.Equal(1, projection.Session.HealEventCount);
            Assert.Equal(2, projection.Powers.Count);
        }
    }

    [Fact]
    public void Unknown_incoming_target_and_unsupported_family_do_not_become_self_magnitude()
    {
        var incoming = Assert.Single(ParseCanonical(IncomingDamage));
        var engine = new CombatEngine();
        engine.Apply(incoming with { Target = ActorRef.Unknown });
        engine.Apply(incoming with { Family = CombatEventFamily.Unparsed });
        engine.Apply(incoming with { Family = CombatEventFamily.EnvironmentDamage });
        var projection = engine.Project();
        Assert.Equal(CombatScaledAmount.Zero, projection.Session.DamageReceived);
        Assert.Empty(projection.Powers);
        Assert.Empty(projection.Targets);
    }

    [Theory]
    [InlineData("segment")]
    [InlineData("binding")]
    [InlineData("context")]
    [InlineData("source")]
    [InlineData("account")]
    [InlineData("name")]
    [InlineData("sequence")]
    public void Recharge_requires_prior_activation_in_exact_scope(string difference)
    {
        var pair = ParseCanonical(HastenActivated, HastenRecharged);
        var candidate = pair[1];
        candidate = difference switch
        {
            "segment" => candidate with { Provenance = candidate.Provenance with { SourceSegmentId = Guid.NewGuid() } },
            "binding" => candidate with { Provenance = candidate.Provenance with { BindingGeneration = 9 } },
            "context" => candidate with { Provenance = candidate.Provenance with { ContextId = MonitoringContextId.CreateNew() } },
            "source" => candidate with { Provenance = candidate.Provenance with { SourceId = "another" } },
            "account" => candidate with { Provenance = candidate.Provenance with { AccountStableId = "another" } },
            "sequence" => candidate with { Provenance = candidate.Provenance with { ParserSequence = 0 } },
            _ => candidate with { PowerName = "hasten" }
        };
        var engine = new CombatEngine();
        engine.Apply(pair[0]);
        engine.Apply(candidate);
        Assert.Equal(0, engine.Project().Session.ConfirmedRechargeCompletedCount);
        Assert.Equal(1, engine.Project().Session.UnmatchedRechargeCandidateCount);
    }

    [Fact]
    public void Recharge_before_activation_is_never_retroactively_confirmed_and_battery_is_unmatched()
    {
        var projection = Project(HastenRecharged, HastenActivated,
            "2026-09-12 05:25:08 The battery is recharged.",
            "2026-09-12 05:25:08 The battery is still recharging.");
        Assert.Equal(0, projection.Session.ConfirmedRechargeCompletedCount);
        Assert.Equal(3, projection.Session.UnmatchedRechargeCandidateCount);
    }

    [Fact]
    public void Power_and_pet_overflow_stays_bounded_with_new_pet_names_and_real_other_name()
    {
        var item = Assert.Single(ParseCanonical(PetBrawl));
        var engine = new CombatEngine();
        for (var i = 0; i < 1500; i++)
            engine.Apply(item with { PowerName = "Power" + i, Actor = ActorRef.OwnPet("Pet" + i) });
        var projection = engine.Project();
        Assert.True(projection.CoverageLimited);
        Assert.InRange(projection.Powers.Count, 1, CombatEngine.MaxTrackedPowers + 6);
        Assert.InRange(projection.Actors.Count, 1, CombatEngine.MaxTrackedPets + 3);
        Assert.Equal(1500 * item.Amount.Hundredths, projection.Session.DamageDealtOwnedPets.Hundredths);
        Assert.Equal(projection.Session.DamageDealtOwnedPets.Hundredths,
            projection.Powers.Where(row => row.Scope == CombatAnalyticsScope.PerPet).Sum(row => row.DamageMagnitude.Hundredths));
    }

    [Fact]
    public void Other_target_does_not_collide_with_overflow_and_pet_rollup_is_case_insensitive()
    {
        var item = Assert.Single(ParseCanonical(PetBrawl));
        var engine = new CombatEngine();
        engine.Apply(item with { Target = ActorRef.UnknownNamed("Other") });
        for (var i = 0; i < CombatEngine.MaxTrackedTargets; i++)
            engine.Apply(item with { Target = ActorRef.UnknownNamed("Target" + i), Actor = ActorRef.OwnPet("imp") });
        var projection = engine.Project();
        Assert.Equal(2, projection.Targets.Count(row => row.NormalizedTargetName == "Other"));
        Assert.Single(projection.Powers, row => row.Scope == CombatAnalyticsScope.PerPet);
        var aggregate = Assert.Single(projection.Powers, row => row.Scope == CombatAnalyticsScope.OwnPetsAggregate);
        Assert.Equal(CombatEngine.MaxTrackedTargets, aggregate.DistinctTargetCount);
        Assert.True(aggregate.CoverageLimited);
        Assert.Equal(projection.Session.DamageDealt.Hundredths, projection.Targets.Sum(row => row.DamageDealt.Hundredths));
    }

    [Fact]
    public void Magnitude_overflow_is_explicit_and_negative_input_is_not_aggregated()
    {
        var item = Assert.Single(ParseCanonical(HotFeet));
        var engine = new CombatEngine();
        engine.Apply(item with { Amount = new CombatScaledAmount(-1) });
        Assert.Equal(CombatScaledAmount.Zero, engine.Project().Session.DamageDealt);
        Assert.True(engine.Project().CoverageLimited);
        engine.Apply(item with { Amount = new CombatScaledAmount(long.MaxValue) });
        Assert.Throws<OverflowException>(() => engine.Apply(item));
    }

    [Fact]
    public void Stream_dedups_separate_calls_preserves_ambiguity_and_resets_per_session()
    {
        var pair = WithComplementaryHealChannels(ParseCanonical(HealReceived, HealDealt));
        var policy = MirrorCompatibilityPolicy.ForTests(TestHealRule());
        var stream = new SessionCombatStream(policy);
        Assert.Empty(stream.Push(pair[0]));
        Assert.Empty(stream.Push(pair[1]));
        Assert.Single(stream.Flush());
        Assert.Empty(stream.Push(pair[1]));
        Assert.False(stream.LastPushAccepted);
        var nextSession = new SessionCombatStream(policy);
        nextSession.Push(pair[1]);
        Assert.Equal(EventFacets.HealDelivered, Assert.Single(nextSession.Flush()).Facets);
        var ambiguous = new SessionCombatStream(policy);
        ambiguous.Push(pair[0]);
        ambiguous.Push(pair[1]);
        ambiguous.Push(pair[0] with { Provenance = pair[0].Provenance with
            { ParserSequence = 3, ByteStart = pair[1].Provenance.ByteEnd, ByteEnd = pair[1].Provenance.ByteEnd + 100 } });
        Assert.Equal(3, ambiguous.Flush().Count);
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

    private static MirrorCompatibilityRule TestHealRule() =>
        new()
        {
            RuleId = "test-heal-dealt-received",
            LeftFamily = CombatEventFamily.HealDealt,
            RightFamily = CombatEventFamily.HealReceived,
            RequiredDiscriminator = MirrorDiscriminatorKind.ActualSourceChannel,
            LeftChannel = "TestDelivery",
            RightChannel = "TestReceipt",
            MaxSequenceDistance = 2,
            FacetUnion = EventFacets.HealDelivered | EventFacets.HealReceived
        };

    private static IReadOnlyList<CanonicalCombatEvent> WithComplementaryHealChannels(
        IReadOnlyList<CanonicalCombatEvent> events) =>
        [
            WithChannel(events[0], events[0].Family == CombatEventFamily.HealReceived ? "TestReceipt" : "TestDelivery"),
            WithChannel(events[1], events[1].Family == CombatEventFamily.HealDealt ? "TestDelivery" : "TestReceipt")
        ];

    private static CanonicalCombatEvent WithChannel(CanonicalCombatEvent canonical, string channel) =>
        canonical with
        {
            SourceChannel = channel,
            MirrorClass = canonical.MirrorClass with { SourceChannel = channel },
            Provenance = canonical.Provenance with { SourceChannel = channel }
        };
}

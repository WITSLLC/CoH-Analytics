using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Slice 4 verified pet-prefix strip, OwnPet remapping of <c>you</c>, and conservative name rollup.
/// </summary>
public sealed class Slice4PlayerPetGrammarTests
{
    [Theory]
    [MemberData(nameof(GoldenData))]
    public void Canonical_fields_match_independent_golden(
        Slice2CanonicalFieldGoldenTests.CanonicalFieldGolden expected)
    {
        Slice2CanonicalFieldGoldenTests.AssertIndependentGolden(expected);
    }

    [Fact]
    public void Independent_goldens_cover_every_CombatGrammarId_exactly_once_with_prior_slices()
    {
        var covered = Slice2CanonicalFieldGoldenTests.CoveredGrammarIds
            .Concat(Slice3PlayerCombatGrammarTests.CoveredGrammarIds)
            .Concat(Goldens.Select(row => row.GrammarId))
            .ToArray();
        Assert.Equal(covered.Length, covered.Distinct().Count());
        Assert.Equal(Enum.GetValues<CombatGrammarId>().Order().ToArray(), covered.Order().ToArray());
    }

    public static TheoryData<Slice2CanonicalFieldGoldenTests.CanonicalFieldGolden> GoldenData
    {
        get
        {
            var data = new TheoryData<Slice2CanonicalFieldGoldenTests.CanonicalFieldGolden>();
            foreach (var row in Goldens)
            {
                data.Add(row);
            }

            return data;
        }
    }

    private static readonly Slice2CanonicalFieldGoldenTests.CanonicalFieldGolden[] Goldens =
    [
        new(
            Line: "2026-09-12 05:36:35 Ally_B HITS you! Particle Burst power had a 57.64% chance to hit and rolled a 22.71.",
            GrammarId: CombatGrammarId.Acc05SourceHitsYouRolled,
            Family: CombatEventFamily.AttackResolution,
            ActorType: ActorType.Unknown,
            ActorDisplayName: "Ally_B",
            TargetType: ActorType.Self,
            TargetDisplayName: null,
            PowerName: "Particle Burst",
            AmountHundredths: 0,
            Magnitude: MagnitudeKind.None,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: CombatAttackOutcome.Hit,
            DisplayedChanceHundredths: 5764,
            RollHundredths: 2271,
            SourceTimestamp: new DateTime(2026, 9, 12, 5, 36, 35, DateTimeKind.Unspecified),
            Facets: EventFacets.AttackResolution),
        new(
            Line: "2026-09-12 05:24:59 Hero_A HITS you! Health power was autohit.",
            GrammarId: CombatGrammarId.Acc06SourceHitsYouAutohit,
            Family: CombatEventFamily.AttackResolution,
            ActorType: ActorType.Unknown,
            ActorDisplayName: "Hero_A",
            TargetType: ActorType.Self,
            TargetDisplayName: null,
            PowerName: "Health",
            AmountHundredths: 0,
            Magnitude: MagnitudeKind.None,
            DamageTypeText: null,
            Delivery: DeliveryFlags.Autohit,
            EffectSuffix: null,
            Outcome: CombatAttackOutcome.Hit,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 9, 12, 5, 24, 59, DateTimeKind.Unspecified),
            Facets: EventFacets.AttackResolution)
    ];

    [Fact]
    public void Channel_chat_colon_is_not_a_pet_prefix()
    {
        const string line =
            "2026-09-12 05:26:30 [NPC] Dancer: You know why this club is so great?  No one has tried to steal my purse ALL NIGHT!";
        var input = CombatEventParserTestSupport.Classify(line, logDate: new DateOnly(2026, 9, 12));
        Assert.Equal(ParserEventKind.SystemLine, input.EventKind);
        Assert.Equal("NPC", input.SourceChannel);
        Assert.False(PetCombatPrefix.TryStrip(Body(input), out _, out _));
        Assert.False(CombatEventParserTestSupport.Parser.TryParseCanonical(input, out _));
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out _));
    }

    [Fact]
    public void Single_space_colon_prefix_is_not_verified_pet_combat()
    {
        const string body = "Imp: You hit Training Dummy with your Fire Ball for 12 points of Fire damage.";
        Assert.False(PetCombatPrefix.TryStrip(body, out _, out _));
        Assert.False(GrammarMatcher.TryMatch(body, out _));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Whitespace_only_pet_name_is_rejected_without_throwing(string entity)
    {
        var body = entity + ":  You hit Cleaner with your Brawl for 35.63 points of Fire damage.";
        Assert.False(PetCombatPrefix.TryStrip(body, out _, out _));
        Assert.False(GrammarMatcher.TryMatch(body, out _));
        Assert.False(TryCanonical("2026-09-12 05:38:43 " + body, out _));
        Assert.False(TryLegacy("2026-09-12 05:38:43 " + body));
    }

    [Fact]
    public void Verified_pet_outgoing_maps_you_to_own_pet_not_self()
    {
        const string line =
            "2026-09-12 05:38:43 Imp:  You hit Cleaner with your Brawl for 35.63 points of Fire damage.";
        Assert.True(TryCanonical(line, out var canonical));
        Assert.Equal(CombatGrammarId.Dmg01YouHitWithPower, canonical.GrammarId);
        Assert.Equal(CombatEventFamily.DamageDealt, canonical.Family);
        Assert.Equal(ActorType.OwnPet, canonical.Actor.Type);
        Assert.Equal("Imp", canonical.Actor.DisplayName);
        Assert.NotEqual(ActorType.Self, canonical.Actor.Type);
        Assert.Equal(ActorType.Unknown, canonical.Target!.Type);
        Assert.Equal("Cleaner", canonical.Target.DisplayName);
        Assert.Equal("Brawl", canonical.PowerName);
        Assert.Equal(new CombatScaledAmount(3563), canonical.Amount);
        Assert.Equal("Fire", canonical.DamageType!.Value.Text);
        AssertPetRollup(canonical.Actor, "Imp");
        Assert.False(TryLegacy(line));
    }

    [Fact]
    public void Verified_pet_incoming_maps_you_to_the_prefixed_pet()
    {
        const string line =
            "2026-09-12 05:38:27 Imp:  Demon Juggernaut hits you with their Particle Burst for 16.43 points of Energy damage.";
        var input = CombatEventParserTestSupport.Classify(line, logDate: new DateOnly(2026, 9, 12));
        Assert.Equal(ParserEventKind.PotentialIdentityEvidence, input.EventKind);
        Assert.False(CharacterIdentityResolver.IsStrongAttributedEvidence(input));
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(input, out var canonical));
        Assert.Equal(CombatGrammarId.Dmg06SourceHitsYouWithTheirPower, canonical.GrammarId);
        Assert.Equal(CombatEventFamily.DamageReceived, canonical.Family);
        Assert.Equal(ActorType.Unknown, canonical.Actor.Type);
        Assert.Equal("Demon Juggernaut", canonical.Actor.DisplayName);
        Assert.Equal(ActorType.OwnPet, canonical.Target!.Type);
        Assert.Equal("Imp", canonical.Target.DisplayName);
        Assert.NotEqual(ActorType.Self, canonical.Target.Type);
        Assert.Equal("Particle Burst", canonical.PowerName);
        Assert.DoesNotContain("their", canonical.PowerName, StringComparison.Ordinal);
        AssertPetRollup(canonical.Target, "Imp");
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out _));
    }

    [Fact]
    public void Pet_heal_received_keeps_source_and_maps_you_to_the_pet()
    {
        const string line =
            "2026-09-12 05:38:25 Imp:  Scourging Blast heals you with their Scourging Blast for 39.21 health points over time.";
        Assert.True(TryCanonical(line, out var canonical));
        Assert.Equal(CombatGrammarId.Heal05SourceHealsYouWithTheirHealthPoints, canonical.GrammarId);
        Assert.Equal(CombatEventFamily.HealReceived, canonical.Family);
        Assert.Equal("Scourging Blast", canonical.Actor.DisplayName);
        Assert.Equal(ActorType.Unknown, canonical.Actor.Type);
        Assert.Equal(ActorType.OwnPet, canonical.Target!.Type);
        Assert.Equal("Imp", canonical.Target.DisplayName);
        Assert.Equal(new CombatScaledAmount(3921), canonical.Amount);
        Assert.Equal(MagnitudeKind.HitPoints, canonical.Magnitude);
        Assert.Equal(DeliveryFlags.DoT, canonical.Delivery);
        Assert.False(TryLegacy(line));
    }

    [Fact]
    public void Pet_endurance_grant_over_time_is_canonical_only()
    {
        const string line =
            "2026-09-12 05:38:25 Imp:  Scourging Blast hits you with their Scourging Blast granting you 5 points of endurance over time.";
        Assert.True(TryCanonical(line, out var canonical));
        Assert.Equal(CombatGrammarId.End02SourceHitsYouGrantingYouEndurance, canonical.GrammarId);
        Assert.Equal(CombatEventFamily.EnduranceGrantReceived, canonical.Family);
        Assert.Equal(MagnitudeKind.Endurance, canonical.Magnitude);
        Assert.Equal(DeliveryFlags.DoT, canonical.Delivery);
        Assert.Equal(ActorType.OwnPet, canonical.Target!.Type);
        Assert.Equal("Imp", canonical.Target.DisplayName);
        Assert.False(CanonicalToLegacyAdapter.TryToLegacy(canonical, out _));
        Assert.False(TryLegacy(line));
    }

    [Fact]
    public void Pet_outgoing_roll_and_incoming_roll_use_pet_scope_for_you()
    {
        Assert.True(TryCanonical(
            "2026-09-12 05:38:43 Imp:  HIT Cleaner! Your Brawl power had a 95.00% chance to hit, you rolled a 17.38.",
            out var outgoing));
        Assert.Equal(CombatGrammarId.Acc01RolledHit, outgoing.GrammarId);
        Assert.Equal(ActorType.OwnPet, outgoing.Actor.Type);
        Assert.Equal("Imp", outgoing.Actor.DisplayName);
        Assert.Equal("Cleaner", outgoing.Target!.DisplayName);
        Assert.Equal(CombatAttackOutcome.Hit, outgoing.Outcome);
        Assert.Equal(9500, outgoing.DisplayedChanceHundredths);
        Assert.Equal(1738, outgoing.RollHundredths);
        Assert.NotEqual(ActorType.Self, outgoing.Actor.Type);

        Assert.True(TryCanonical(
            "2026-09-12 05:38:27 Imp:  Demon Juggernaut HITS you! Particle Burst power had a 89.53% chance to hit and rolled a 83.25.",
            out var incoming));
        Assert.Equal(CombatGrammarId.Acc05SourceHitsYouRolled, incoming.GrammarId);
        Assert.Equal("Demon Juggernaut", incoming.Actor.DisplayName);
        Assert.Equal(ActorType.Unknown, incoming.Actor.Type);
        Assert.Equal(ActorType.OwnPet, incoming.Target!.Type);
        Assert.Equal("Imp", incoming.Target.DisplayName);
        Assert.Equal(8953, incoming.DisplayedChanceHundredths);
        Assert.Equal(8325, incoming.RollHundredths);
        Assert.False(TryLegacy(
            "2026-09-12 05:38:27 Imp:  Demon Juggernaut HITS you! Particle Burst power had a 89.53% chance to hit and rolled a 83.25."));
    }

    [Fact]
    public void Essence_pet_normalizes_as_own_pet_without_lore_classification()
    {
        Assert.True(TryCanonical(
            "2026-09-12 05:42:17 Defiler Essence:  You hit Sweeper with your Doublehit for 67.04 points of Energy damage.",
            out var canonical));
        Assert.Equal(ActorType.OwnPet, canonical.Actor.Type);
        Assert.Equal("Defiler Essence", canonical.Actor.DisplayName);
        Assert.Equal("Sweeper", canonical.Target!.DisplayName);
        Assert.Equal("Doublehit", canonical.PowerName);
        AssertPetRollup(canonical.Actor, "Defiler Essence");
        AssertNoPetClassificationTypes();
    }

    [Fact]
    public void Enervating_storm_is_own_pet_scope_without_permanent_semantics()
    {
        Assert.True(TryCanonical(
            "2026-09-12 05:42:17 Enervating Storm:  You hit Lifter with your Enervating Storm for 4.31 points of Negative Energy damage.",
            out var canonical));
        Assert.Equal(ActorType.OwnPet, canonical.Actor.Type);
        Assert.Equal("Enervating Storm", canonical.Actor.DisplayName);
        Assert.Equal("Negative Energy", canonical.DamageType!.Value.Text);
        AssertPetRollup(canonical.Actor, "Enervating Storm");
        Assert.Equal(ActorType.Unknown, canonical.Target!.Type);
        AssertNoPetClassificationTypes();
    }

    [Fact]
    public void Essence_incoming_autohit_does_not_mark_the_source_as_other_pet()
    {
        Assert.True(TryCanonical(
            "2026-09-12 05:42:17 Ravager Essence:  Defiler Essence HITS you! Empowering Burst power was autohit.",
            out var canonical));
        Assert.Equal(CombatGrammarId.Acc06SourceHitsYouAutohit, canonical.GrammarId);
        Assert.Equal("Defiler Essence", canonical.Actor.DisplayName);
        Assert.Equal(ActorType.Unknown, canonical.Actor.Type);
        Assert.NotEqual(ActorType.OtherPet, canonical.Actor.Type);
        Assert.Equal(ActorType.OwnPet, canonical.Target!.Type);
        Assert.Equal("Ravager Essence", canonical.Target.DisplayName);
        Assert.Equal(DeliveryFlags.Autohit, canonical.Delivery);
    }

    [Fact]
    public void Same_normalized_pet_name_rolls_up_and_leaves_instance_unresolved()
    {
        Assert.True(TryCanonical(
            "2026-09-12 05:38:43 Imp:  You hit Cleaner with your Brawl for 35.63 points of Fire damage.",
            out var first));
        Assert.True(TryCanonical(
            "2026-09-12 05:38:47 Imp:  You hit Cleaner with your Brawl for 15.61 points of Fire damage over time.",
            out var second));
        Assert.Equal(first.Actor.PetKey!.NormalizedPetName, second.Actor.PetKey!.NormalizedPetName);
        Assert.Null(first.Actor.PetKey.InstanceOrdinal);
        Assert.Null(second.Actor.PetKey.InstanceOrdinal);
        Assert.True(first.Actor.PetKey.CoverageLimited);
        Assert.True(second.Actor.PetKey.CoverageLimited);
        Assert.Null(first.DuplicateOf);
        Assert.Null(second.DuplicateOf);
    }

    [Fact]
    public void Non_pet_you_still_maps_to_self()
    {
        Assert.True(TryCanonical(
            "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
            out var canonical));
        Assert.Equal(ActorType.Self, canonical.Actor.Type);
        Assert.Null(canonical.Actor.PetKey);
        Assert.Equal(ActorType.Unknown, canonical.Target!.Type);
        Assert.Equal("Lusca", canonical.Target.DisplayName);
    }

    [Fact]
    public void Player_heal_of_a_target_named_Imp_is_not_pet_scope()
    {
        Assert.True(TryCanonical(
            "2026-09-12 05:36:59 You heal Imp with Transfusion for 421.34 health points.",
            out var canonical));
        Assert.Equal(CombatGrammarId.Heal04YouHealTargetHealthPoints, canonical.GrammarId);
        Assert.Equal(ActorType.Self, canonical.Actor.Type);
        Assert.Equal(ActorType.Unknown, canonical.Target!.Type);
        Assert.Equal("Imp", canonical.Target.DisplayName);
        Assert.Null(canonical.Target.PetKey);
        Assert.True(TryLegacy("2026-09-12 05:36:59 You heal Imp with Transfusion for 421.34 health points."));
    }

    [Fact]
    public void Pet_prefix_does_not_attribute_defeats()
    {
        const string body = "Imp:  You have defeated Cleaner.";
        Assert.True(PetCombatPrefix.TryStrip(body, out var entity, out var inner));
        Assert.Equal("Imp", entity);
        Assert.Equal("You have defeated Cleaner.", inner);
        Assert.False(GrammarMatcher.TryMatch(body, out _));
        Assert.Empty(GrammarMatcher.EnumerateMatches(body));
        Assert.False(TryCanonical("2026-09-12 05:00:00 " + body, out _));
    }

    [Fact]
    public void Non_combat_pet_prefixed_boost_line_stays_unparsed()
    {
        const string line = "2026-09-12 05:38:19 Imp:  The Hero_A boosts the damage of your attacks!";
        Assert.False(TryCanonical(line, out _));
        Assert.False(TryLegacy(line));
    }

    [Fact]
    public void Incoming_player_roll_is_canonical_only_and_does_not_count_as_player_accuracy()
    {
        const string line =
            "2026-09-12 05:36:35 Ally_B HITS you! Particle Burst power had a 57.64% chance to hit and rolled a 22.71.";
        Assert.True(TryCanonical(line, out var canonical));
        Assert.Equal(ActorType.Self, canonical.Target!.Type);
        Assert.False(TryLegacy(line));
        var aggregator = new CombatAggregator();
        var input = CombatEventParserTestSupport.Classify(line, logDate: new DateOnly(2026, 9, 12));
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out var legacy));
        Assert.Null(legacy);
        Assert.Equal(0, aggregator.EventsApplied);
    }

    [Fact]
    public void Pet_events_do_not_enter_legacy_aggregators()
    {
        var aggregator = new CombatAggregator();
        var line = "2026-09-12 05:38:43 Imp:  You hit Cleaner with your Brawl for 35.63 points of Fire damage.";
        var input = CombatEventParserTestSupport.Classify(line, logDate: new DateOnly(2026, 9, 12));
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out _));
        var snapshot = aggregator.ToSnapshot(
            new DateTimeOffset(2026, 9, 12, 5, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 12, 5, 1, 0, TimeSpan.Zero),
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);
        Assert.Equal(0, snapshot.DamageDealt.Hundredths);
        Assert.Equal(0, aggregator.EventsApplied);
    }

    [Fact]
    public void Scorch_remains_deferred_and_no_proc_or_dedup_fields_are_added()
    {
        Assert.False(GrammarMatcher.TryMatch(
            "The Hot Feet scorches you for 12 points of Fire damage!",
            out _));
        var names = typeof(CanonicalCombatEvent).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Contains("Proc", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Parent", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("DuplicateOf", names);
    }

    private static void AssertPetRollup(ActorRef actor, string displayName)
    {
        Assert.NotNull(actor.PetKey);
        Assert.Equal(CharacterIdentityResolver.NormalizeName(displayName), actor.PetKey.NormalizedPetName);
        Assert.Null(actor.PetKey.InstanceOrdinal);
        Assert.Null(actor.PetKey.OwnerRecordId);
        Assert.True(actor.PetKey.CoverageLimited);
    }

    private static void AssertNoPetClassificationTypes()
    {
        var typeNames = typeof(ActorRef).Assembly.GetTypes().Select(type => type.Name).ToArray();
        Assert.DoesNotContain("PetClassification", typeNames);
    }

    private static bool TryCanonical(string line, out CanonicalCombatEvent canonicalEvent)
    {
        var logDate = line.StartsWith("2026-09-12", StringComparison.Ordinal)
            ? new DateOnly(2026, 9, 12)
            : new DateOnly(2026, 8, 4);
        var input = CombatEventParserTestSupport.Classify(line, logDate: logDate);
        return CombatEventParserTestSupport.Parser.TryParseCanonical(input, out canonicalEvent);
    }

    private static bool TryLegacy(string line)
    {
        var logDate = line.StartsWith("2026-09-12", StringComparison.Ordinal)
            ? new DateOnly(2026, 9, 12)
            : new DateOnly(2026, 8, 4);
        return CombatEventParserTestSupport.Parser.TryParse(
            CombatEventParserTestSupport.Classify(line, logDate: logDate),
            out _);
    }

    private static string Body(ParserEvent input)
    {
        Assert.True(ParserLineEnvelope.TryGetBody(input.RawLine, input.SourceId.LogDate, out var body));
        return body;
    }
}

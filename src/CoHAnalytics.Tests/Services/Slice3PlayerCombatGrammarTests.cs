using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Slice 3 player/non-pet grammar expansion: live heals, incoming with-their, multiword types,
/// endurance, mez/knock, companion-miss canonical retention, and identity dual-role routing.
/// </summary>
public sealed class Slice3PlayerCombatGrammarTests
{
    [Theory]
    [MemberData(nameof(GoldenData))]
    public void Canonical_fields_match_independent_golden(
        Slice2CanonicalFieldGoldenTests.CanonicalFieldGolden expected)
    {
        Slice2CanonicalFieldGoldenTests.AssertIndependentGolden(expected);
    }

    [Fact]
    public void Independent_goldens_cover_every_CombatGrammarId_exactly_once_with_slice2()
    {
        var covered = Slice2CanonicalFieldGoldenTests.CoveredGrammarIds
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
            Line: "2026-09-12 05:36:59 You heal Imp with Transfusion for 421.34 health points.",
            GrammarId: CombatGrammarId.Heal04YouHealTargetHealthPoints,
            Family: CombatEventFamily.HealDealt,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: ActorType.Unknown,
            TargetDisplayName: "Imp",
            PowerName: "Transfusion",
            AmountHundredths: 42134,
            Magnitude: MagnitudeKind.HitPoints,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 9, 12, 5, 36, 59, DateTimeKind.Unspecified),
            Facets: EventFacets.HealDelivered),
        new(
            Line: "2026-09-12 05:24:58 Hero_A heals you with their Panacea: Chance for +Hit Points/Endurance for 78.93 health points.",
            GrammarId: CombatGrammarId.Heal05SourceHealsYouWithTheirHealthPoints,
            Family: CombatEventFamily.HealReceived,
            ActorType: ActorType.Unknown,
            ActorDisplayName: "Hero_A",
            TargetType: ActorType.Self,
            TargetDisplayName: null,
            PowerName: "Panacea: Chance for +Hit Points/Endurance",
            AmountHundredths: 7893,
            Magnitude: MagnitudeKind.HitPoints,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 9, 12, 5, 24, 58, DateTimeKind.Unspecified),
            Facets: EventFacets.HealReceived),
        new(
            Line: "2026-09-12 05:36:35 Ally_B hits you with their Particle Burst for 31.64 points of Energy damage.",
            GrammarId: CombatGrammarId.Dmg06SourceHitsYouWithTheirPower,
            Family: CombatEventFamily.DamageReceived,
            ActorType: ActorType.Unknown,
            ActorDisplayName: "Ally_B",
            TargetType: ActorType.Self,
            TargetDisplayName: null,
            PowerName: "Particle Burst",
            AmountHundredths: 3164,
            Magnitude: MagnitudeKind.HitPoints,
            DamageTypeText: "Energy",
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 9, 12, 5, 36, 35, DateTimeKind.Unspecified),
            Facets: EventFacets.DamageReceived),
        new(
            Line: "2026-09-12 05:24:59 You hit Hero_A with your Panacea: Chance for +Hit Points/Endurance granting them 7.5 points of endurance.",
            GrammarId: CombatGrammarId.End01YouHitGrantingThemEndurance,
            Family: CombatEventFamily.EnduranceGrantDealt,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: ActorType.Unknown,
            TargetDisplayName: "Hero_A",
            PowerName: "Panacea: Chance for +Hit Points/Endurance",
            AmountHundredths: 750,
            Magnitude: MagnitudeKind.Endurance,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 9, 12, 5, 24, 59, DateTimeKind.Unspecified),
            Facets: EventFacets.EnduranceGrantDealt),
        new(
            Line: "2026-09-12 05:24:59 Hero_A hits you with their Panacea: Chance for +Hit Points/Endurance granting you 7.5 points of endurance.",
            GrammarId: CombatGrammarId.End02SourceHitsYouGrantingYouEndurance,
            Family: CombatEventFamily.EnduranceGrantReceived,
            ActorType: ActorType.Unknown,
            ActorDisplayName: "Hero_A",
            TargetType: ActorType.Self,
            TargetDisplayName: null,
            PowerName: "Panacea: Chance for +Hit Points/Endurance",
            AmountHundredths: 750,
            Magnitude: MagnitudeKind.Endurance,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 9, 12, 5, 24, 59, DateTimeKind.Unspecified),
            Facets: EventFacets.EnduranceGrantReceived),
        new(
            Line: "2026-09-12 05:36:30 You Hold Sweeper with your Gravitational Anchor: Chance for Hold.",
            GrammarId: CombatGrammarId.Mez01YouStatusTargetWithPower,
            Family: CombatEventFamily.Mez,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: ActorType.Unknown,
            TargetDisplayName: "Sweeper",
            PowerName: "Gravitational Anchor: Chance for Hold",
            AmountHundredths: 0,
            Magnitude: MagnitudeKind.None,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 9, 12, 5, 36, 30, DateTimeKind.Unspecified),
            Facets: EventFacets.Mez)
        {
            StatusName = "Hold"
        },
        new(
            Line: "2026-09-12 05:37:34 You knock Cleaner off their feet with your Ragnarok: Chance for Knockdown!",
            GrammarId: CombatGrammarId.Knk01YouKnockTargetOffFeet,
            Family: CombatEventFamily.Knock,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: ActorType.Unknown,
            TargetDisplayName: "Cleaner",
            PowerName: "Ragnarok: Chance for Knockdown",
            AmountHundredths: 0,
            Magnitude: MagnitudeKind.None,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 9, 12, 5, 37, 34, DateTimeKind.Unspecified),
            Facets: EventFacets.Knock),
        new(
            Line: "2026-08-06 12:00:03 Fire Cages missed!",
            GrammarId: CombatGrammarId.Cmp01CompanionMissSummary,
            Family: CombatEventFamily.CompanionMissSummary,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: null,
            TargetDisplayName: null,
            PowerName: "Fire Cages",
            AmountHundredths: 0,
            Magnitude: MagnitudeKind.None,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 6, 12, 0, 3, DateTimeKind.Unspecified),
            Facets: EventFacets.CompanionMissSummary)
    ];

    [Fact]
    public void Live_self_heal_received_and_delivered_remain_separate_source_events()
    {
        const string received =
            "2026-09-12 05:24:58 Hero_A heals you with their Panacea: Chance for +Hit Points/Endurance for 78.93 health points.";
        const string delivered =
            "2026-09-12 05:24:59 You heal Hero_A with Panacea: Chance for +Hit Points/Endurance for 78.93 health points.";
        Assert.True(TryCanonical(received, out var receivedEvent));
        Assert.True(TryCanonical(delivered, out var deliveredEvent));
        Assert.Equal(CombatEventFamily.HealReceived, receivedEvent.Family);
        Assert.Equal(CombatEventFamily.HealDealt, deliveredEvent.Family);
        Assert.Null(receivedEvent.DuplicateOf);
        Assert.Null(deliveredEvent.DuplicateOf);
        Assert.NotEqual(receivedEvent.GrammarId, deliveredEvent.GrammarId);
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(
            CombatEventParserTestSupport.Classify(received), out var receivedLegacy));
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(
            CombatEventParserTestSupport.Classify(delivered), out var deliveredLegacy));
        Assert.Equal(CombatEventKind.HealingReceived, receivedLegacy.Kind);
        Assert.Equal(CombatEventKind.HealingDealt, deliveredLegacy.Kind);
    }

    [Fact]
    public void Live_heal_power_names_keep_punctuation_spaces_colon_plus_and_slash()
    {
        Assert.True(TryCanonical(
            "2026-09-12 05:24:58 Hell's Vengence heals you with their Panacea: Chance for +Hit Points/Endurance for 78.93 health points.",
            out var received));
        Assert.Equal("Hell's Vengence", received.Actor.DisplayName);
        Assert.Equal("Panacea: Chance for +Hit Points/Endurance", received.PowerName);
        Assert.Equal(ActorType.Self, received.Target!.Type);

        Assert.True(TryCanonical(
            "2026-09-12 05:24:59 You heal Hell's Vengence with Panacea: Chance for +Hit Points/Endurance for 78.93 health points.",
            out var delivered));
        Assert.Equal("Hell's Vengence", delivered.Target!.DisplayName);
        Assert.Equal("Panacea: Chance for +Hit Points/Endurance", delivered.PowerName);
        Assert.Equal(CombatEventFamily.HealDealt, delivered.Family);
    }

    [Fact]
    public void Incoming_with_their_unresistable_unique_preserves_type_flags_and_excludes_their_from_power()
    {
        const string line =
            "2026-09-12 05:48:06 Spear Damage hits you with their Energized Halberd Strike for 50.48 points of unresistable Unique damage.";
        Assert.True(TryCanonical(line, out var canonical));
        Assert.Equal(CombatGrammarId.Dmg06SourceHitsYouWithTheirPower, canonical.GrammarId);
        Assert.Equal("Spear Damage", canonical.Actor.DisplayName);
        Assert.Equal(ActorType.Self, canonical.Target!.Type);
        Assert.Equal("Energized Halberd Strike", canonical.PowerName);
        Assert.True(canonical.DamageType is { Text: "Unique", IsUnresistable: true, IsUnique: true });
        Assert.Equal(new CombatScaledAmount(5048), canonical.Amount);
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(
            CombatEventParserTestSupport.Classify(line), out var legacy));
        Assert.Equal(CombatEventKind.DamageReceived, legacy.Kind);
        Assert.Equal("Unique", legacy.DamageType);
        Assert.DoesNotContain("their", legacy.PowerName, StringComparison.Ordinal);
    }

    [Fact]
    public void Incoming_with_their_dot_does_not_swallow_over_time_into_damage_type()
    {
        const string line =
            "2026-09-12 05:36:35 Ally_B hits you with their Particle Burst for 10.54 points of Energy damage over time.";
        Assert.True(TryCanonical(line, out var canonical));
        Assert.Equal("Energy", canonical.DamageType!.Value.Text);
        Assert.Equal(DeliveryFlags.DoT, canonical.Delivery);
        Assert.Null(canonical.EffectSuffix);
    }

    [Fact]
    public void Expanded_type_capture_keeps_containment_on_existing_outgoing_dot()
    {
        const string line =
            "2026-08-04 12:00:02 You hit Lusca with your Fire Cages for 10.36 points of Fire damage over time (CONTAINMENT).";
        Assert.True(TryCanonical(line, out var canonical));
        Assert.Equal(CombatGrammarId.Dmg01YouHitWithPower, canonical.GrammarId);
        Assert.Equal("Fire", canonical.DamageType!.Value.Text);
        Assert.False(canonical.DamageType.Value.IsUnresistable);
        Assert.Equal(DeliveryFlags.DoT | DeliveryFlags.Containment, canonical.Delivery);
        Assert.Equal("CONTAINMENT", canonical.EffectSuffix);
    }

    [Fact]
    public void Player_shaped_negative_energy_type_is_preserved_as_multiword_text()
    {
        const string line =
            "2026-08-04 12:00:00 You hit Builder with your Frigid Beam for 105.08 points of Negative Energy damage over time.";
        Assert.True(TryCanonical(line, out var canonical));
        Assert.Equal(CombatGrammarId.Dmg01YouHitWithPower, canonical.GrammarId);
        Assert.Equal("Negative Energy", canonical.DamageType!.Value.Text);
        Assert.Equal(DeliveryFlags.DoT, canonical.Delivery);
    }

    [Fact]
    public void PotentialIdentityEvidence_dual_role_keeps_classification_and_normalizes_combat()
    {
        const string line =
            "2026-09-12 05:36:35 Ally_B hits you with their Particle Burst for 31.64 points of Energy damage.";
        var input = CombatEventParserTestSupport.Classify(line, logDate: new DateOnly(2026, 9, 12));
        Assert.Equal(ParserEventKind.PotentialIdentityEvidence, input.EventKind);
        Assert.Equal("system_attributed_action", input.ClassificationRuleId);
        Assert.Equal("Ally_B", input.StructuralEvidence!.CandidateName);
        Assert.False(CharacterIdentityResolver.IsStrongAttributedEvidence(input));
        Assert.Null(CharacterIdentityResolver.GetStrongCandidateName(input));
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(input, out var canonical));
        Assert.Equal(CombatEventFamily.DamageReceived, canonical.Family);
        Assert.Equal("Particle Burst", canonical.PowerName);
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(input, out var legacy));
        Assert.Equal(CombatEventKind.DamageReceived, legacy.Kind);
        Assert.False(CombatEventParser.IsCombatCandidate(input.EventKind, Body(input)));
        Assert.True(CombatEventParser.IsCanonicalCombatCandidate(input.EventKind, Body(input)));
    }

    [Fact]
    public void Endurance_grants_are_canonical_only_and_are_not_heals()
    {
        const string dealt =
            "2026-09-12 05:24:59 You hit Hero_A with your Panacea: Chance for +Hit Points/Endurance granting them 7.5 points of endurance.";
        const string received =
            "2026-09-12 05:24:59 Hero_A hits you with their Panacea: Chance for +Hit Points/Endurance granting you 7.5 points of endurance.";
        Assert.True(TryCanonical(dealt, out var dealtEvent));
        Assert.True(TryCanonical(received, out var receivedEvent));
        Assert.Equal(MagnitudeKind.Endurance, dealtEvent.Magnitude);
        Assert.Equal(MagnitudeKind.Endurance, receivedEvent.Magnitude);
        Assert.NotEqual(CombatEventFamily.HealDealt, dealtEvent.Family);
        Assert.NotEqual(CombatEventFamily.HealReceived, receivedEvent.Family);
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(
            CombatEventParserTestSupport.Classify(dealt, logDate: new DateOnly(2026, 9, 12)), out _));
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(
            CombatEventParserTestSupport.Classify(received, logDate: new DateOnly(2026, 9, 12)), out _));
        Assert.False(CanonicalToLegacyAdapter.TryToLegacy(dealtEvent, out _));
        Assert.False(CanonicalToLegacyAdapter.TryToLegacy(receivedEvent, out _));
    }

    [Fact]
    public void Mez_overpower_and_stun_are_canonical_counts_only()
    {
        Assert.True(TryCanonical(
            "2026-09-12 05:36:30 You Immobilize Mender with your Fire Cages (OVERPOWER).",
            out var immobilize));
        Assert.Equal(CombatEventFamily.Mez, immobilize.Family);
        Assert.Equal("Immobilize", immobilize.StatusName);
        Assert.Equal("Mender", immobilize.Target!.DisplayName);
        Assert.Equal("Fire Cages", immobilize.PowerName);
        Assert.Equal(DeliveryFlags.Overpower, immobilize.Delivery);
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(
            CombatEventParserTestSupport.Classify(
                "2026-09-12 05:36:30 You Immobilize Mender with your Fire Cages (OVERPOWER).",
                logDate: new DateOnly(2026, 9, 12)),
            out _));

        Assert.True(TryCanonical(
            "2026-09-12 05:36:34 You Stun Sweeper with your Pyronic Radial Final Judgement.",
            out var stun));
        Assert.Equal("Stun", stun.StatusName);
        Assert.Equal(DeliveryFlags.None, stun.Delivery);
        Assert.False(CanonicalToLegacyAdapter.TryToLegacy(stun, out _));
    }

    [Fact]
    public void Companion_miss_is_canonical_only_and_does_not_change_legacy_unparsed()
    {
        var input = CombatEventParserTestSupport.Classify("2026-08-06 12:00:03 Fire Cages missed!");
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out _));
        Assert.False(CombatEventParser.IsCombatShapedUnparsed(input));
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(input, out var canonical));
        Assert.Equal(CombatEventFamily.CompanionMissSummary, canonical.Family);
        Assert.False(CanonicalToLegacyAdapter.TryToLegacy(canonical, out _));
    }

    [Fact]
    public void Environmental_scorch_and_you_take_remain_unsupported()
    {
        Assert.False(GrammarMatcher.TryMatch(
            "The Hot Feet scorches you for 12 points of Fire damage!",
            out _));
        var youTake = CombatEventParserTestSupport.Classify(
            "2026-08-04 12:00:00 You take 12 points of Toxic damage from Poison Gas.");
        Assert.False(CombatEventParserTestSupport.Parser.TryParseCanonical(youTake, out _));
        Assert.True(CombatEventParser.IsCombatShapedUnparsed(youTake));
    }

    [Theory]
    [InlineData("Imp:  You hit Training Dummy with your Fire Ball for 12 points of Fire damage.")]
    [InlineData("Imp:  Demon Juggernaut hits you with their Particle Burst for 16.43 points of Energy damage.")]
    [InlineData("Imp:  Scourging Blast hits you with their Scourging Blast granting you 5 points of endurance over time.")]
    [InlineData("Imp:  Scourging Blast heals you with their Scourging Blast for 39.21 health points over time.")]
    [InlineData("Defiler Essence:  You hit Builder with your Frigid Beam for 12 points of Fire damage.")]
    [InlineData("Enervating Storm:  You hit Lifter with your Enervating Storm for 4.31 points of Negative Energy damage.")]
    [InlineData("Ravager Essence:  You hit Builder with your Frigid Beam for 105.08 points of Negative Energy damage over time.")]
    [InlineData("Imp:  HIT Cleaner! Your Brawl power had a 95.00% chance to hit, you rolled a 17.38.")]
    [InlineData("Imp:  Demon Juggernaut HITS you! Particle Burst power had a 89.53% chance to hit and rolled a 83.25.")]
    [InlineData("Ravager Essence:  Defiler Essence HITS you! Empowering Burst power was autohit.")]
    public void Pet_prefixed_lines_are_outside_slice3_player_grammars(string body)
    {
        Assert.False(GrammarMatcher.TryMatch(body, out _));
        Assert.Empty(GrammarMatcher.EnumerateMatches(body));
        var input = CombatEventParserTestSupport.Classify("2026-09-12 05:00:00 " + body, logDate: new DateOnly(2026, 9, 12));
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out _));
        Assert.False(CombatEventParserTestSupport.Parser.TryParseCanonical(input, out _));
    }

    [Fact]
    public void Old_incoming_without_their_still_uses_dmg03()
    {
        const string line =
            "2026-08-04 12:00:00 Crey Thorn Mook hits you with Bone Shard for 22.15 points of Lethal damage.";
        Assert.True(TryCanonical(line, out var canonical));
        Assert.Equal(CombatGrammarId.Dmg03SourceHitsYouWithPower, canonical.GrammarId);
        Assert.Equal("Bone Shard", canonical.PowerName);
    }

    [Fact]
    public void Old_heal_word_order_is_not_reinterpreted_as_live_health_points_grammar()
    {
        const string line =
            "2026-08-04 12:00:00 You heal Example Ally for 78.50 hit points with Healing Aura.";
        Assert.True(TryCanonical(line, out var canonical));
        Assert.Equal(CombatGrammarId.Heal01YouHealTarget, canonical.GrammarId);
    }

    [Fact]
    public void Malformed_decimal_amount_does_not_normalize_new_heal_or_their_damage()
    {
        Assert.True(GrammarMatcher.TryMatch(
            "You heal Imp with Transfusion for 421.345 health points.",
            out var healMatch));
        Assert.Equal(CombatGrammarId.Heal04YouHealTargetHealthPoints, healMatch.GrammarId);
        Assert.False(Normalizer.TryNormalize(
            healMatch,
            CombatEventParserTestSupport.Classify(
                "2026-08-04 12:00:00 You heal Imp with Transfusion for 421.345 health points."),
            out _));

        Assert.True(GrammarMatcher.TryMatch(
            "Ally_B hits you with their Particle Burst for 31.645 points of Energy damage.",
            out var damageMatch));
        Assert.Equal(CombatGrammarId.Dmg06SourceHitsYouWithTheirPower, damageMatch.GrammarId);
        Assert.False(Normalizer.TryNormalize(
            damageMatch,
            CombatEventParserTestSupport.Classify(
                "2026-08-04 12:00:00 Ally_B hits you with their Particle Burst for 31.645 points of Energy damage."),
            out _));
    }

    [Fact]
    public void Arbitrary_chat_is_not_combat()
    {
        var chat = CombatEventParserTestSupport.Classify(
            "2026-08-04 12:00:00 [Team] Ally_A: You hit Lifter with Transfusion.");
        Assert.Equal(ParserEventKind.ChatLine, chat.EventKind);
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(chat, out _));
        Assert.False(CombatEventParserTestSupport.Parser.TryParseCanonical(chat, out _));
    }

    [Fact]
    public void Proc_like_power_name_is_not_attributed_beyond_the_surfaced_text()
    {
        Assert.True(TryCanonical(
            "2026-09-12 05:24:59 You heal Hero_A with Panacea: Chance for +Hit Points/Endurance for 78.93 health points.",
            out var canonical));
        Assert.Equal("Panacea: Chance for +Hit Points/Endurance", canonical.PowerName);
        Assert.Null(canonical.DuplicateOf);
        var names = typeof(CanonicalCombatEvent).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Contains("Proc", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Parent", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("BuildConfirmed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void New_canonical_only_families_are_not_applied_to_legacy_aggregators()
    {
        var aggregator = new CombatAggregator();
        foreach (var line in new[]
                 {
                     "2026-09-12 05:24:59 You hit Hero_A with your Panacea: Chance for +Hit Points/Endurance granting them 7.5 points of endurance.",
                     "2026-09-12 05:36:30 You Hold Sweeper with your Gravitational Anchor: Chance for Hold.",
                     "2026-09-12 05:37:34 You knock Cleaner off their feet with your Ragnarok: Chance for Knockdown!",
                     "2026-08-06 12:00:03 Fire Cages missed!"
                 })
        {
            var input = CombatEventParserTestSupport.Classify(line, logDate: new DateOnly(2026, 9, 12));
            Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out var legacy));
            Assert.Null(legacy);
        }

        var snapshot = aggregator.ToSnapshot(
            new DateTimeOffset(2026, 9, 12, 5, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 12, 5, 1, 0, TimeSpan.Zero),
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);
        Assert.Equal(0, snapshot.HealingDealt.Hundredths);
        Assert.Equal(0, snapshot.HealingReceived.Hundredths);
        Assert.Equal(0, snapshot.DamageDealt.Hundredths);
        Assert.Equal(0, snapshot.DamageReceived.Hundredths);
        Assert.Equal(0, aggregator.EventsApplied);
    }

    [Fact]
    public void DamageType_from_parsed_token_title_cases_ordinary_and_flags_unresistable_unique()
    {
        Assert.Equal(new DamageType("Fire"), DamageType.FromParsedToken("Fire"));
        Assert.Equal(new DamageType("Lethal"), DamageType.FromParsedToken("lethal"));
        Assert.Equal(new DamageType("Negative Energy"), DamageType.FromParsedToken("Negative Energy"));
        Assert.Equal(new DamageType("Unique", isUnresistable: true, isUnique: true),
            DamageType.FromParsedToken("unresistable Unique"));
    }

    private static bool TryCanonical(string line, out CanonicalCombatEvent canonicalEvent)
    {
        var logDate = line.StartsWith("2026-09-12", StringComparison.Ordinal)
            ? new DateOnly(2026, 9, 12)
            : new DateOnly(2026, 8, 4);
        var input = CombatEventParserTestSupport.Classify(line, logDate: logDate);
        return CombatEventParserTestSupport.Parser.TryParseCanonical(input, out canonicalEvent);
    }

    private static string Body(ParserEvent input)
    {
        Assert.True(ParserLineEnvelope.TryGetBody(input.RawLine, input.SourceId.LogDate, out var body));
        return body;
    }
}

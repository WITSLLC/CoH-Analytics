using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Slice 5A explicit power lifecycle telemetry. Recharge observations are canonical-only.
/// </summary>
public sealed class Slice5APowerLifecycleTests
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
            .Concat(Slice4PlayerPetGrammarTests.CoveredGrammarIds)
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

    internal static readonly Slice2CanonicalFieldGoldenTests.CanonicalFieldGolden[] Goldens =
    [
        new(
            Line: "2026-09-12 05:25:08 Hasten is recharged.",
            GrammarId: CombatGrammarId.Act03PowerIsRecharged,
            Family: CombatEventFamily.RechargeCandidate,
            ActorType: ActorType.Unknown,
            ActorDisplayName: null,
            TargetType: null,
            TargetDisplayName: null,
            PowerName: "Hasten",
            AmountHundredths: 0,
            Magnitude: MagnitudeKind.None,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 9, 12, 5, 25, 8, DateTimeKind.Unspecified),
            Facets: EventFacets.RechargeCandidate,
            PowerStateTransition: PowerStateTransition.RechargeCompletedObserved),
        new(
            Line: "2026-09-12 05:36:20 Fire Cages is still recharging.",
            GrammarId: CombatGrammarId.Act04PowerIsStillRecharging,
            Family: CombatEventFamily.RechargeCandidate,
            ActorType: ActorType.Unknown,
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
            SourceTimestamp: new DateTime(2026, 9, 12, 5, 36, 20, DateTimeKind.Unspecified),
            Facets: EventFacets.RechargeCandidate,
            PowerStateTransition: PowerStateTransition.StillRechargingObserved)
    ];

    [Fact]
    public void Activation_normalizes_hasten()
    {
        Assert.True(TryCanonical("2026-09-12 05:25:01 You activated the Hasten power.", out var canonical));
        Assert.Equal(CombatGrammarId.Act02YouActivatedThePower, canonical.GrammarId);
        Assert.Equal(CombatEventFamily.Activation, canonical.Family);
        Assert.Equal(PowerStateTransition.Activated, canonical.PowerStateTransition);
        Assert.Equal("Hasten", canonical.PowerName);
        Assert.Equal(ActorType.Self, canonical.Actor.Type);
        Assert.Null(canonical.Target);
        Assert.Equal(CombatScaledAmount.Zero, canonical.Amount);
        Assert.Equal(MagnitudeKind.None, canonical.Magnitude);
        Assert.Equal(4, canonical.Provenance.GrammarSetVersion);
    }

    [Fact]
    public void Recharge_complete_normalizes_hasten()
    {
        Assert.True(TryCanonical("2026-09-12 05:25:08 Hasten is recharged.", out var canonical));
        Assert.Equal(CombatGrammarId.Act03PowerIsRecharged, canonical.GrammarId);
        Assert.Equal(PowerStateTransition.RechargeCompletedObserved, canonical.PowerStateTransition);
        Assert.Equal("Hasten", canonical.PowerName);
        Assert.Equal(ActorType.Unknown, canonical.Actor.Type);
        Assert.Null(canonical.Actor.DisplayName);
    }

    [Fact]
    public void Still_recharging_normalizes_fire_cages()
    {
        Assert.True(TryCanonical("2026-09-12 05:36:20 Fire Cages is still recharging.", out var canonical));
        Assert.Equal(CombatGrammarId.Act04PowerIsStillRecharging, canonical.GrammarId);
        Assert.Equal(PowerStateTransition.StillRechargingObserved, canonical.PowerStateTransition);
        Assert.Equal("Fire Cages", canonical.PowerName);
    }

    [Fact]
    public void Multiword_power_recharge_preserves_exact_name()
    {
        Assert.True(TryCanonical(
            "2026-09-12 05:40:10 Long Range Teleporter is recharged.",
            out var canonical));
        Assert.Equal("Long Range Teleporter", canonical.PowerName);
        Assert.Equal(PowerStateTransition.RechargeCompletedObserved, canonical.PowerStateTransition);
    }

    [Fact]
    public void Activation_then_recharge_retains_order_and_provenance()
    {
        var activatedInput = Classify("2026-09-12 05:25:01 You activated the Hasten power.", sequence: 1, byteStart: 0);
        var rechargedInput = Classify("2026-09-12 05:25:08 Hasten is recharged.", sequence: 2, byteStart: 60);
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(activatedInput, out var activated));
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(rechargedInput, out var recharged));
        Assert.True(activated.SourceTimestamp < recharged.SourceTimestamp);
        Assert.Equal(activated.Provenance.SourceId, recharged.Provenance.SourceId);
        Assert.Equal(1, activated.Provenance.ParserSequence);
        Assert.Equal(2, recharged.Provenance.ParserSequence);
        Assert.NotEqual(activated.Provenance.ByteStart, recharged.Provenance.ByteStart);
        Assert.Equal("Hasten", activated.PowerName);
        Assert.Equal("Hasten", recharged.PowerName);
    }

    [Fact]
    public void Repeated_recharge_complete_events_remain_distinct()
    {
        var first = Classify("2026-09-12 05:40:10 Long Range Teleporter is recharged.", sequence: 1, byteStart: 0);
        var second = Classify("2026-09-12 05:40:10 Long Range Teleporter is recharged.", sequence: 2, byteStart: 80);
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(first, out var left));
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(second, out var right));
        var result = new Deduplicator().Deduplicate([left, right]);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
        Assert.All(result.Events, item => Assert.Null(item.DuplicateOf));
        Assert.All(result.Events, item => Assert.Equal(PowerStateTransition.RechargeCompletedObserved, item.PowerStateTransition));
    }

    [Fact]
    public void Repeated_still_recharging_events_remain_distinct()
    {
        var first = Classify("2026-09-12 05:36:20 Fire Cages is still recharging.", sequence: 1, byteStart: 0);
        var second = Classify("2026-09-12 05:36:21 Fire Cages is still recharging.", sequence: 2, byteStart: 50);
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(first, out var left));
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(second, out var right));
        var result = new Deduplicator().Deduplicate([left, right]);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
        Assert.All(result.Events, item => Assert.Null(item.DuplicateOf));
    }

    [Fact]
    public void Power_state_events_do_not_fabricate_combat_magnitudes_or_enter_dedup_rules()
    {
        Assert.True(TryCanonical("2026-09-12 05:25:08 Hasten is recharged.", out var recharged));
        Assert.True(TryCanonical("2026-09-12 05:36:20 Fire Cages is still recharging.", out var blocked));
        Assert.Null(recharged.DamageType);
        Assert.Null(blocked.DamageType);
        Assert.Equal(MagnitudeKind.None, recharged.Magnitude);
        Assert.Equal(CombatScaledAmount.Zero, recharged.Amount);
        Assert.Null(recharged.Outcome);
        Assert.NotEqual(EventFacets.DamageDealt, recharged.Facets);
        Assert.NotEqual(EventFacets.HealDelivered, recharged.Facets);
        Assert.NotEqual(EventFacets.EnduranceGrantDealt, recharged.Facets);
        Assert.NotEqual(EventFacets.AttackResolution, recharged.Facets);
        Assert.False(MirrorCompatibilityPolicy.Version1.IsPhaseAEligible(CombatEventFamily.RechargeCandidate));
        Assert.False(MirrorCompatibilityPolicy.Version1.IsPhaseAEligible(CombatEventFamily.Activation));
        Assert.Equal(1, DedupPolicyVersion.Current);
    }

    [Fact]
    public void Recharge_forms_are_canonical_only_and_do_not_change_legacy_activation()
    {
        const string activated = "2026-09-12 05:25:01 You activated the Hasten power.";
        const string recharged = "2026-09-12 05:25:08 Hasten is recharged.";
        const string still = "2026-09-12 05:36:20 Fire Cages is still recharging.";
        Assert.True(CombatEventParserTestSupport.TryParseLine(activated, out var legacy));
        Assert.Equal(CombatEventKind.PowerActivation, legacy.Kind);
        Assert.Equal("Hasten", legacy.PowerName);
        Assert.False(CombatEventParserTestSupport.TryParseLine(recharged, out _));
        Assert.False(CombatEventParserTestSupport.TryParseLine(still, out _));
        Assert.True(TryCanonical(recharged, out _));
        Assert.True(TryCanonical(still, out _));
        Assert.False(CombatEventParser.IsCombatShapedUnparsed(
            CombatEventParserTestSupport.Classify(recharged)));
        Assert.False(CombatEventParser.IsCombatShapedUnparsed(
            CombatEventParserTestSupport.Classify(still)));
    }

    [Fact]
    public void Pet_prefixed_combat_is_unchanged_and_chat_recharge_is_not_matched()
    {
        Assert.True(TryCanonical(
            "2026-09-12 05:38:43 Imp:  You hit Cleaner with your Brawl for 35.63 points of Fire damage.",
            out var petHit));
        Assert.Equal(ActorType.OwnPet, petHit.Actor.Type);
        Assert.Equal(CombatEventFamily.DamageDealt, petHit.Family);
        var chat = CombatEventParserTestSupport.Classify(
            "2026-09-12 05:25:08 [Team] Ally_A: Hasten is recharged.");
        Assert.Equal(ParserEventKind.ChatLine, chat.EventKind);
        Assert.False(CombatEventParserTestSupport.Parser.TryParseCanonical(chat, out _));
    }

    [Fact]
    public void Fixture_lines_normalize_in_source_order()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Combat", "power-lifecycle-2026-09-12.tsv");
        var lines = File.ReadAllLines(path)
            .Select(line => line[(line.IndexOf('\t') + 1)..])
            .ToArray();
        Assert.Equal(7, lines.Length);
        var events = new List<CanonicalCombatEvent>();
        foreach (var line in lines)
        {
            Assert.True(TryCanonical(line, out var canonical));
            events.Add(canonical);
        }

        Assert.Equal(PowerStateTransition.Activated, events[0].PowerStateTransition);
        Assert.Equal(PowerStateTransition.RechargeCompletedObserved, events[1].PowerStateTransition);
        Assert.Equal(PowerStateTransition.StillRechargingObserved, events[2].PowerStateTransition);
        Assert.Equal(PowerStateTransition.StillRechargingObserved, events[3].PowerStateTransition);
        Assert.Equal("Long Range Teleporter", events[4].PowerName);
        Assert.Equal(events[5].PowerName, events[6].PowerName);
        Assert.Equal(2, events.Count(item => item.PowerName == "Long Range Teleporter"
            && item.PowerStateTransition == PowerStateTransition.RechargeCompletedObserved));
    }

    [Fact]
    public void GrammarSetVersion_is_four_and_dedup_policy_stays_one()
    {
        Assert.Equal(4, EventProvenance.CurrentGrammarSetVersion);
        Assert.Equal(1, DedupPolicyVersion.Current);
        Assert.True(TryCanonical("2026-09-12 05:25:08 Hasten is recharged.", out var canonical));
        Assert.Equal(4, canonical.Provenance.GrammarSetVersion);
    }

    [Theory]
    [InlineData("You activate Hasten.", "Hasten")]
    [InlineData("You activated the Hasten power.", "Hasten")]
    [InlineData("You activated the Long Range Teleporter power.", "Long Range Teleporter")]
    [InlineData("You activated the The battery power.", "The battery")]
    public void Direct_player_activation_independently_establishes_the_surfaced_name(string body, string name)
    {
        Assert.True(TryCanonical("2026-09-12 05:25:01 " + body, out var activation));
        Assert.True(activation.IsPlayerPowerActivationEvidence);
        Assert.Equal(name, activation.PowerName);
        Assert.Equal(PowerStateTransition.Activated, activation.PowerStateTransition);
    }

    [Theory]
    [InlineData("The battery is recharged.", PowerStateTransition.RechargeCompletedObserved)]
    [InlineData("The battery is still recharging.", PowerStateTransition.StillRechargingObserved)]
    public void Arbitrary_prose_is_only_an_unconfirmed_candidate(string body, PowerStateTransition wording)
    {
        Assert.True(TryCanonical("2026-09-12 05:25:08 " + body, out var candidate));
        Assert.Equal("The battery", candidate.PowerName);
        AssertCandidate(candidate, wording);
    }

    [Theory]
    [InlineData("Hasten", "is recharged.", PowerStateTransition.RechargeCompletedObserved)]
    [InlineData("Hasten", "is still recharging.", PowerStateTransition.StillRechargingObserved)]
    [InlineData("Long Range Teleporter", "is recharged.", PowerStateTransition.RechargeCompletedObserved)]
    [InlineData("Rune's Gift-X", "is recharged.", PowerStateTransition.RechargeCompletedObserved)]
    public void Prior_activation_preserves_correlatable_names_but_does_not_confirm_in_stateless_parser(
        string name, string suffix, PowerStateTransition wording)
    {
        var activationInput = Classify("2026-09-12 05:25:01 You activated the " + name + " power.", 1, 0);
        var candidateInput = Classify("2026-09-12 05:25:08 " + name + " " + suffix, 2, 200) with
        {
            ContextId = activationInput.ContextId,
            SourceSegmentId = activationInput.SourceSegmentId
        };
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(activationInput, out var activation));
        Assert.True(activation.IsPlayerPowerActivationEvidence);
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(candidateInput, out var candidate));
        Assert.Equal(activation.PowerName, candidate.PowerName);
        Assert.Equal(name, candidate.PowerName);
        AssertCandidate(candidate, wording);
        Assert.Equal(candidateInput.ContextId, candidate.Provenance.ContextId);
        Assert.Equal(candidateInput.SourceId.Value, candidate.Provenance.SourceId);
        Assert.Equal(candidateInput.SourceId.AccountStableId, candidate.Provenance.AccountStableId);
        Assert.Equal(candidateInput.SourceSegmentId.Value, candidate.Provenance.SourceSegmentId);
        Assert.Equal(candidateInput.BindingGeneration, candidate.Provenance.BindingGeneration);
        Assert.Equal(candidateInput.Sequence, candidate.Provenance.ParserSequence);
        Assert.Equal(candidateInput.SourceByteStart, candidate.Provenance.ByteStart);
        Assert.Equal(candidateInput.SourceByteEnd, candidate.Provenance.ByteEnd);
        Assert.Equal(candidateInput.ObservedAt, candidate.Provenance.ObservedAt);
        Assert.Equal(candidateInput.SourceTimestamp, candidate.Provenance.SourceTimestamp);
        Assert.Equal(candidateInput.ClassificationRuleId, candidate.Provenance.ClassificationRuleId);
        Assert.Equal(candidateInput.SourceChannel, candidate.Provenance.SourceChannel);
        Assert.Equal(4, candidate.Provenance.GrammarSetVersion);
    }

    [Fact]
    public void Activation_evidence_cannot_leak_across_welcome_or_source_context()
    {
        var activation = Classify("2026-09-12 05:25:01 You activated the Hasten power.", 1, 0);
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(activation, out var activated));
        Assert.True(activated.IsPlayerPowerActivationEvidence);
        // The normalizer receives no gameplay-session id. Even unchanged source scope must
        // never imply confirmation across a Welcome; the session-aware consumer is deferred.
        var welcome = Classify("2026-09-12 05:25:05 Welcome to City of Heroes, Hero_A!", 2, 100) with
        {
            ContextId = activation.ContextId, SourceSegmentId = activation.SourceSegmentId
        };
        Assert.False(CombatEventParserTestSupport.Parser.TryParseCanonical(welcome, out _));
        var next = Classify("2026-09-12 05:25:08 Hasten is recharged.", 3, 200);
        foreach (var input in new[] { next, next with
                 { ContextId = activation.ContextId, SourceSegmentId = activation.SourceSegmentId } })
        {
            Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(input, out var candidate));
            AssertCandidate(candidate, PowerStateTransition.RechargeCompletedObserved);
        }
    }

    [Fact]
    public void Pet_activation_does_not_establish_a_player_power()
    {
        Assert.True(TryCanonical("2026-09-12 05:25:01 Imp:  You activate Hasten.", out var pet));
        Assert.Equal(ActorType.OwnPet, pet.Actor.Type);
        Assert.False(pet.IsPlayerPowerActivationEvidence);
    }

    private static void AssertCandidate(CanonicalCombatEvent candidate, PowerStateTransition wording)
    {
        Assert.Equal(CombatEventFamily.RechargeCandidate, candidate.Family);
        Assert.Equal(EventFacets.RechargeCandidate, candidate.Facets);
        Assert.Equal(wording, candidate.PowerStateTransition);
        Assert.False(candidate.IsPlayerPowerActivationEvidence);
        Assert.Equal(ActorRef.Unknown, candidate.Actor);
        Assert.Null(candidate.Target);
        Assert.Equal(MagnitudeKind.None, candidate.Magnitude);
        Assert.Equal(CombatScaledAmount.Zero, candidate.Amount);
        Assert.Null(candidate.DamageType);
        Assert.Null(candidate.Outcome);
        Assert.Null(candidate.DisplayedChanceHundredths);
        Assert.Null(candidate.RollHundredths);
        Assert.Equal(DeliveryFlags.None, candidate.Delivery);
        Assert.False(CanonicalToLegacyAdapter.TryToLegacy(candidate, out _));
        Assert.False(MirrorCompatibilityPolicy.Version1.IsPhaseAEligible(candidate.Family));
    }

    private static bool TryCanonical(string line, out CanonicalCombatEvent canonicalEvent) =>
        CombatEventParserTestSupport.Parser.TryParseCanonical(
            CombatEventParserTestSupport.Classify(line),
            out canonicalEvent);

    private static ParserEvent Classify(string line, long sequence, long byteStart)
    {
        return CombatEventParserTestSupport.Classifier.Classify(new ParserRawEvent
        {
            ContextId = MonitoringContextId.CreateNew(),
            SourceId = LogSourceId.Create(
                "acct-1",
                "acct-1",
                "C:\\fake\\chatlog.txt",
                new DateOnly(2026, 9, 12)),
            SourceSegmentId = ParserSourceSegmentId.CreateNew(),
            BindingGeneration = 1,
            SourceTransitionKind = MonitoringSourceTransitionKind.SourceAssigned,
            Sequence = sequence,
            ObservedAt = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero).AddSeconds(sequence),
            RawLine = line,
            SourceByteStart = byteStart,
            SourceByteEnd = byteStart + line.Length + 2,
            LineStatus = ParserLineStatus.Complete
        });
    }
}

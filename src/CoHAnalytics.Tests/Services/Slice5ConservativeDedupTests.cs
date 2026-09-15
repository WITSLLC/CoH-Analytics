using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Slice 5 conservative allowlist dedup. Production v1 is empty; collapse tests use an
/// explicit test-only policy and never enable an unproven production rule.
/// </summary>
public sealed class Slice5ConservativeDedupTests
{
    private const string PanaceaReceived =
        "2026-09-12 05:24:58 Hero_A heals you with their Panacea: Chance for +Hit Points/Endurance for 78.93 health points.";

    private const string PanaceaDelivered =
        "2026-09-12 05:24:59 You heal Hero_A with Panacea: Chance for +Hit Points/Endurance for 78.93 health points.";

    private const string TransfusionReceived =
        "2026-09-12 05:36:59 Hero_A heals you with their Transfusion for 421.34 health points.";

    private const string TransfusionDelivered =
        "2026-09-12 05:36:59 You heal Hero_A with Transfusion for 421.34 health points.";

    private const string TransfusionDeliveredAllyB =
        "2026-09-12 05:36:59 You heal Ally_B with Transfusion for 421.34 health points.";

    private const string FireCagesTick =
        "2026-09-12 05:36:30 You hit Cleaner with your Fire Cages for 2.57 points of Fire damage over time.";

    private const string HotFeetTick =
        "2026-09-12 05:38:44 You hit Dismantler with your Hot Feet for 19.71 points of Fire damage.";

    private const string PetBrawlTick =
        "2026-09-12 05:38:47 Imp:  You hit Cleaner with your Brawl for 15.61 points of Fire damage over time.";

    private const string EnduranceDealt =
        "2026-09-12 05:24:59 You hit Hero_A with your Panacea: Chance for +Hit Points/Endurance granting them 7.5 points of endurance.";

    private const string EnduranceReceived =
        "2026-09-12 05:24:59 Hero_A hits you with their Panacea: Chance for +Hit Points/Endurance granting you 7.5 points of endurance.";

    private static readonly Deduplicator Production = new();

    [Fact]
    public void DedupPolicyVersion_v1_is_empty_allowlist()
    {
        Assert.Equal(1, DedupPolicyVersion.Current);
        Assert.Equal(DedupPolicyVersion.Current, MirrorCompatibilityPolicy.Version1.Version);
        Assert.Empty(MirrorCompatibilityPolicy.Version1.EnabledRules);
        Assert.Contains(
            MirrorCompatibilityPolicy.DeferredRelationships,
            item => item.LeftFamily == CombatEventFamily.HealDealt
                && item.RightFamily == CombatEventFamily.HealReceived);
        Assert.Contains(
            MirrorCompatibilityPolicy.DeferredRelationships,
            item => item.Reason.Contains("Player/pet", StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_allowlist_keeps_candidate_heal_halves_separate()
    {
        var events = ParseCanonical(PanaceaReceived, PanaceaDelivered);
        var result = Production.Deduplicate(events);

        Assert.Equal(2, result.Events.Count);
        Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
        Assert.Equal(0, result.Diagnostics.MirrorCandidatesHeldCount);
        Assert.All(result.Events, item => Assert.Null(item.DuplicateOf));
        Assert.Equal(CombatEventFamily.HealReceived, result.Events[0].Family);
        Assert.Equal(CombatEventFamily.HealDealt, result.Events[1].Family);
        Assert.Equal(EventFacets.HealReceived, result.Events[0].Facets);
        Assert.Equal(EventFacets.HealDelivered, result.Events[1].Facets);
        Assert.Equal(result.Events[0].Amount, result.Events[1].Amount);
    }

    [Fact]
    public void Empty_allowlist_does_not_collapse_same_second_transfusion_candidates()
    {
        var events = ParseCanonical(TransfusionReceived, TransfusionDelivered);
        var result = Production.Deduplicate(events);

        Assert.Equal(2, result.Events.Count);
        Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
        Assert.All(result.Events, item => Assert.Null(item.SourceChannel));
        Assert.All(result.Events, item => Assert.Null(item.DuplicateOf));
    }

    [Fact]
    public void Heal_mirror_rule_stays_disabled_without_independent_proof()
    {
        Assert.DoesNotContain(
            MirrorCompatibilityPolicy.Version1.EnabledRules,
            rule => rule.MatchesFamilies(CombatEventFamily.HealDealt, CombatEventFamily.HealReceived));

        var result = Production.Deduplicate(ParseCanonical(TransfusionReceived, TransfusionDelivered));
        Assert.Equal(2, LogicalMagnitudes(result).Count);
        Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
    }

    [Fact]
    public void Pet_player_mirror_rule_stays_disabled_without_independent_proof()
    {
        var events = ParseCanonical(
            "2026-09-12 05:38:43 Imp:  You hit Cleaner with your Brawl for 35.63 points of Fire damage.",
            "2026-09-12 05:38:44 You hit Dismantler with your Hot Feet for 19.71 points of Fire damage.");
        var result = Production.Deduplicate(events);

        Assert.Equal(2, result.Events.Count);
        Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
        Assert.Equal(ActorType.OwnPet, result.Events[0].Actor.Type);
        Assert.Equal(ActorType.Self, result.Events[1].Actor.Type);
        Assert.All(result.Events, item => Assert.Null(item.DuplicateOf));
    }

    [Fact]
    public void Identical_same_second_player_damage_ticks_remain_two()
    {
        var events = ParseCanonical(FireCagesTick, FireCagesTick);
        var result = Production.Deduplicate(events);

        Assert.Equal(2, result.Events.Count);
        Assert.Equal(CombatEventFamily.DamageDealt, result.Events[0].Family);
        Assert.Equal(CombatEventFamily.DamageDealt, result.Events[1].Family);
        Assert.Equal(result.Events[0].Amount, result.Events[1].Amount);
        Assert.Equal(result.Events[0].PowerName, result.Events[1].PowerName);
        Assert.Null(result.Events[0].DuplicateOf);
        Assert.Null(result.Events[1].DuplicateOf);
        Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
    }

    [Fact]
    public void Identical_same_second_pet_hits_remain_two()
    {
        var events = ParseCanonical(PetBrawlTick, PetBrawlTick);
        var result = Production.Deduplicate(events);

        Assert.Equal(2, result.Events.Count);
        Assert.All(result.Events, item => Assert.Equal(ActorType.OwnPet, item.Actor.Type));
        Assert.All(result.Events, item => Assert.Null(item.DuplicateOf));
        Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
    }

    [Fact]
    public void Non_allowlisted_same_shape_endurance_grants_remain_separate()
    {
        var events = ParseCanonical(EnduranceDealt, EnduranceReceived);
        var result = Production.Deduplicate(events);

        Assert.Equal(2, result.Events.Count);
        Assert.Equal(CombatEventFamily.EnduranceGrantDealt, result.Events[0].Family);
        Assert.Equal(CombatEventFamily.EnduranceGrantReceived, result.Events[1].Family);
        Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
        Assert.Equal(0, result.Diagnostics.MirrorCandidatesHeldCount);
    }

    [Fact]
    public void Test_policy_collapses_heal_mirror_only_with_independent_channel_proof()
    {
        var events = WithComplementaryHealChannels(ParseCanonical(TransfusionReceived, TransfusionDelivered));
        var result = new Deduplicator(MirrorCompatibilityPolicy.ForTests(TestHealRule())).Deduplicate(events);

        Assert.Equal(1, result.Diagnostics.MirrorCollapsedCount);
        Assert.Equal(0, result.Diagnostics.MirrorCandidatesHeldCount);
        var survivor = Assert.Single(result.Events, item => item.DuplicateOf is null);
        var duplicate = Assert.Single(result.Events, item => item.DuplicateOf is not null);
        Assert.Equal(EventFacets.HealDelivered | EventFacets.HealReceived, survivor.Facets);
        Assert.Equal(new CombatScaledAmount(42134), survivor.Amount);
        Assert.Equal(new CombatScaledAmount(42134), duplicate.Amount);
        Assert.NotEqual(survivor.Amount.Hundredths * 2, duplicate.Amount.Hundredths);
        Assert.Equal(survivor.Provenance.ParserSequence, duplicate.DuplicateOf!.ParserSequence);
        Assert.Equal(survivor.Provenance.SourceId, duplicate.DuplicateOf.SourceId);
        Assert.Equal(survivor.Provenance.SourceSegmentId, duplicate.DuplicateOf.SourceSegmentId);
        Assert.Equal(survivor.Provenance.BindingGeneration, duplicate.DuplicateOf.BindingGeneration);
        Assert.True(survivor.Provenance.ParserSequence < duplicate.Provenance.ParserSequence);
        Assert.True(duplicate.Facets is EventFacets.HealDelivered or EventFacets.HealReceived);
        Assert.NotEqual(survivor.Facets, duplicate.Facets);
        Assert.Single(LogicalMagnitudes(result));
    }

    [Fact]
    public void Dedup_survivor_has_no_repeat_count_merge_artifact()
    {
        Assert.DoesNotContain(
            typeof(CanonicalCombatEvent).GetProperties(),
            property => property.Name.Contains("Repeat", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(DedupResult).GetProperties(),
            property => property.Name.Contains("Repeat", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(DedupDiagnostics).GetProperties(),
            property => property.Name.Contains("Repeat", StringComparison.OrdinalIgnoreCase));

        var result = new Deduplicator(MirrorCompatibilityPolicy.ForTests(TestHealRule()))
            .Deduplicate(WithComplementaryHealChannels(ParseCanonical(TransfusionReceived, TransfusionDelivered)));
        var survivor = Assert.Single(result.Events, item => item.DuplicateOf is null);
        Assert.Null(survivor.GetType().GetProperty("RepeatCount"));
        Assert.Equal(new CombatScaledAmount(42134), survivor.Amount);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("actor")]
    [InlineData("discriminator")]
    [InlineData("sequence")]
    [InlineData("power")]
    public void Allowlisted_pair_is_held_when_any_phase_b_requirement_fails(string failureMode)
    {
        CanonicalCombatEvent left;
        CanonicalCombatEvent right;
        if (failureMode == "actor")
        {
            var pair = ParseCanonical(TransfusionReceived, TransfusionDeliveredAllyB);
            left = WithChannel(pair[0], "TestReceipt");
            right = WithChannel(pair[1], "TestDelivery");
        }
        else
        {
            var pair = ParseCanonical(TransfusionReceived, TransfusionDelivered);
            left = WithChannel(pair[0], "TestReceipt");
            right = WithChannel(pair[1], "TestDelivery");
        }

        switch (failureMode)
        {
            case "amount":
                right = right with { Amount = new CombatScaledAmount(1) };
                break;
            case "actor":
                break;
            case "discriminator":
                left = left with
                {
                    SourceChannel = null,
                    MirrorClass = left.MirrorClass with { SourceChannel = null },
                    Provenance = left.Provenance with { SourceChannel = null }
                };
                break;
            case "sequence":
                right = right with
                {
                    Sequence = 12,
                    Provenance = right.Provenance with { ParserSequence = 12 }
                };
                break;
            case "power":
                right = right with { PowerName = "Transfusion Other" };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failureMode), failureMode, null);
        }

        var result = new Deduplicator(MirrorCompatibilityPolicy.ForTests(TestHealRule()))
            .Deduplicate([left, right]);

        Assert.Equal(2, result.Events.Count);
        Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
        Assert.True(result.Diagnostics.MirrorCandidatesHeldCount >= 1);
        Assert.All(result.Events, item => Assert.Null(item.DuplicateOf));
    }

    [Fact]
    public void Enabled_heal_rule_does_not_merge_non_allowlisted_damage_shape()
    {
        var events = ParseCanonical(HotFeetTick, HotFeetTick);
        var result = new Deduplicator(MirrorCompatibilityPolicy.ForTests(TestHealRule())).Deduplicate(events);

        Assert.Equal(2, result.Events.Count);
        Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
        Assert.Equal(0, result.Diagnostics.MirrorCandidatesHeldCount);
    }

    [Fact]
    public void Same_family_rule_cannot_be_constructed()
    {
        Assert.Throws<ArgumentException>(() => MirrorCompatibilityPolicy.ForTests(new MirrorCompatibilityRule
        {
            RuleId = "illegal-damage-damage",
            LeftFamily = CombatEventFamily.DamageDealt,
            RightFamily = CombatEventFamily.DamageDealt,
            RequiredDiscriminator = MirrorDiscriminatorKind.ActualSourceChannel,
            LeftChannel = "A",
            RightChannel = "B",
            FacetUnion = EventFacets.DamageDealt
        }));
    }

    [Fact]
    public void Legacy_scalar_parse_still_emits_both_heal_halves()
    {
        Assert.True(CombatEventParserTestSupport.TryParseLine(PanaceaReceived, out var received));
        Assert.True(CombatEventParserTestSupport.TryParseLine(PanaceaDelivered, out var delivered));
        Assert.Equal(CombatEventKind.HealingReceived, received.Kind);
        Assert.Equal(CombatEventKind.HealingDealt, delivered.Kind);
        Assert.Equal(received.Amount, delivered.Amount);
    }

    [Fact]
    public void GrammarSetVersion_is_unchanged_by_dedup_policy()
    {
        var canonical = Assert.Single(ParseCanonical(HotFeetTick));
        Assert.Equal(EventProvenance.CurrentGrammarSetVersion, canonical.Provenance.GrammarSetVersion);
        Assert.Equal(1, DedupPolicyVersion.Current);
        Assert.NotEqual(EventProvenance.CurrentGrammarSetVersion, DedupPolicyVersion.Current);
    }

    [Fact]
    public void Max_channel_fixture_does_not_collapse_any_pair_under_empty_v1_policy()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Combat", "max-channel-2026-09-12.tsv");
        var lines = File.ReadAllLines(path)
            .Select(line => line[(line.IndexOf('\t') + 1)..])
            .ToArray();
        var canonical = new List<CanonicalCombatEvent>();
        var segment = ParserSourceSegmentId.CreateNew();
        var contextId = MonitoringContextId.CreateNew();
        long sequence = 1;
        long byteStart = 0;
        foreach (var line in lines)
        {
            var classified = Classify(line, contextId, segment, sequence, byteStart);
            if (CombatEventParserTestSupport.Parser.TryParseCanonical(classified, out var parsed))
            {
                canonical.Add(parsed);
            }

            byteStart = classified.SourceByteEnd;
            sequence++;
        }

        Assert.True(canonical.Count > 10);
        var result = Production.Deduplicate(canonical);
        Assert.Equal(canonical.Count, result.Events.Count);
        Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
        Assert.Equal(0, result.Diagnostics.MirrorCandidatesHeldCount);
        Assert.All(result.Events, item => Assert.Null(item.DuplicateOf));
    }

    [Fact]
    public void Production_deduplicator_does_not_rewire_legacy_try_parse()
    {
        var input = CombatEventParserTestSupport.Classify(PanaceaDelivered);
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(input, out var legacy));
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(input, out var canonical));
        Assert.Null(canonical.DuplicateOf);
        Assert.Equal(legacy.Amount, CanonicalToLegacyAdapter.ToLegacy(canonical).Amount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("TestReceipt")]
    public void Invalid_rule_channels_are_rejected(string? channel)
    {
        Assert.Throws<ArgumentException>(() =>
            MirrorCompatibilityPolicy.ForTests(TestHealRule() with { LeftChannel = channel! }));
    }

    [Fact]
    public void Duplicate_rules_and_reversed_pairs_are_rejected_and_policy_is_a_snapshot()
    {
        var rule = TestHealRule();
        Assert.Throws<ArgumentException>(() => MirrorCompatibilityPolicy.ForTests(rule, rule));
        Assert.Throws<ArgumentException>(() => MirrorCompatibilityPolicy.ForTests(rule, rule with
        {
            RuleId = "reversed", LeftFamily = rule.RightFamily, RightFamily = rule.LeftFamily,
            LeftChannel = rule.RightChannel, RightChannel = rule.LeftChannel
        }));
        Assert.Throws<ArgumentException>(() => MirrorCompatibilityPolicy.ForTests(rule, rule with
        {
            LeftFamily = CombatEventFamily.EnduranceGrantDealt,
            RightFamily = CombatEventFamily.EnduranceGrantReceived
        }));
        var rules = new[] { rule };
        var policy = MirrorCompatibilityPolicy.ForTests(rules);
        rules[0] = rule with { LeftChannel = "changed" };
        Assert.Equal("TestDelivery", Assert.Single(policy.EnabledRules).LeftChannel);
        Assert.Empty(MirrorCompatibilityPolicy.Version1.EnabledRules);
    }

    [Theory]
    [InlineData("null-channel")]
    [InlineData("empty-channel")]
    [InlineData("whitespace-channel")]
    [InlineData("swapped-channels")]
    [InlineData("channel-provenance")]
    [InlineData("null-power")]
    [InlineData("whitespace-power")]
    [InlineData("null-target")]
    [InlineData("null-timestamps")]
    [InlineData("one-timestamp")]
    [InlineData("timestamp-precision")]
    [InlineData("magnitude")]
    [InlineData("damage-type")]
    [InlineData("same-offsets")]
    [InlineData("overlapping-offsets")]
    [InlineData("same-sequence")]
    [InlineData("source")]
    [InlineData("segment")]
    [InlineData("binding")]
    [InlineData("context")]
    [InlineData("account")]
    public void Missing_or_conflicting_evidence_keeps_both(string missing)
    {
        var pair = WithComplementaryHealChannels(ParseCanonical(TransfusionReceived, TransfusionDelivered));
        var left = pair[0];
        var right = pair[1];
        var differentScope = false;
        switch (missing)
        {
            case "null-channel": right = WithChannel(right, null!); break;
            case "empty-channel": right = WithChannel(right, ""); break;
            case "whitespace-channel": right = WithChannel(right, " "); break;
            case "swapped-channels":
                left = WithChannel(left, "TestDelivery");
                right = WithChannel(right, "TestReceipt");
                break;
            case "channel-provenance": right = right with { Provenance = right.Provenance with { SourceChannel = null } }; break;
            case "null-power": left = left with { PowerName = null }; right = right with { PowerName = null }; break;
            case "whitespace-power": left = left with { PowerName = " " }; right = right with { PowerName = " " }; break;
            case "null-target": right = right with { Target = null }; break;
            case "null-timestamps":
                left = left with { SourceTimestamp = null };
                right = right with { SourceTimestamp = null, ObservedAt = left.ObservedAt };
                break;
            case "one-timestamp": right = right with { SourceTimestamp = null }; break;
            case "timestamp-precision": right = right with { SourceTimestamp = left.SourceTimestamp!.Value.AddTicks(1) }; break;
            case "magnitude": right = right with { Magnitude = MagnitudeKind.Endurance }; break;
            case "damage-type": right = right with { DamageType = Assert.Single(ParseCanonical(HotFeetTick)).DamageType }; break;
            case "same-offsets": right = right with { Provenance = right.Provenance with { ByteStart = left.Provenance.ByteStart, ByteEnd = left.Provenance.ByteEnd } }; break;
            case "overlapping-offsets": right = right with { Provenance = right.Provenance with { ByteStart = left.Provenance.ByteEnd - 1 } }; break;
            case "same-sequence": right = right with { Provenance = right.Provenance with { ParserSequence = left.Provenance.ParserSequence } }; break;
            case "source": right = right with { Provenance = right.Provenance with { SourceId = "other" } }; differentScope = true; break;
            case "segment": right = right with { Provenance = right.Provenance with { SourceSegmentId = Guid.NewGuid() } }; differentScope = true; break;
            case "binding": right = right with { Provenance = right.Provenance with { BindingGeneration = 2 } }; differentScope = true; break;
            case "context": right = right with { Provenance = right.Provenance with { ContextId = MonitoringContextId.CreateNew() } }; differentScope = true; break;
            case "account": right = right with { Provenance = right.Provenance with { AccountStableId = "other" } }; differentScope = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(missing));
        }
        var result = new Deduplicator(MirrorCompatibilityPolicy.ForTests(TestHealRule())).Deduplicate([left, right]);
        Assert.Equal(new[] { left, right }, result.Events);
        Assert.Equal(2, result.LogicalEvents.Count());
        Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
        Assert.Equal(differentScope ? 0 : 1, result.Diagnostics.MirrorCandidatesHeldCount);
        AssertDiagnostics(result.Diagnostics);
    }

    [Fact]
    public void Logical_view_counts_one_magnitude_and_each_facet_once_and_is_idempotent()
    {
        var pair = WithComplementaryHealChannels(ParseCanonical(TransfusionReceived, TransfusionDelivered));
        var dedup = new Deduplicator(MirrorCompatibilityPolicy.ForTests(TestHealRule()));
        var result = dedup.Deduplicate(pair.Reverse().ToArray());
        var survivor = Assert.Single(result.LogicalEvents);
        Assert.Equal(pair[0].Provenance, survivor.Provenance);
        Assert.Equal(42134, result.LogicalEvents.Sum(item => item.Amount.Hundredths));
        Assert.Single(result.LogicalEvents, item => item.Facets.HasFlag(EventFacets.HealDelivered));
        Assert.Single(result.LogicalEvents, item => item.Facets.HasFlag(EventFacets.HealReceived));
        Assert.Equal(2, result.Events.Count);
        Assert.Equal(pair[1].Provenance, result.Events[0].Provenance);
        Assert.Equal(pair[0].Provenance.ParserSequence, result.Events[0].DuplicateOf!.ParserSequence);
        AssertDiagnostics(result.Diagnostics);
        var repeated = dedup.Deduplicate(result.Events);
        Assert.Equal(result.Events, repeated.Events);
        Assert.Equal(0, repeated.Diagnostics.MirrorCollapsedCount);
        Assert.Equal(0, repeated.Diagnostics.MirrorCandidatesHeldCount);
    }

    [Fact]
    public void Multiple_proven_counterparts_are_held_in_every_input_order()
    {
        var events = ParseCanonical(TransfusionReceived, TransfusionDelivered, TransfusionReceived)
            .Select(item => WithChannel(item, item.Family == CombatEventFamily.HealReceived ? "TestReceipt" : "TestDelivery"))
            .ToArray();
        int[][] orders = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        foreach (var order in orders)
        {
            var input = order.Select(index => events[index]).ToArray();
            var result = new Deduplicator(MirrorCompatibilityPolicy.ForTests(TestHealRule())).Deduplicate(input);
            Assert.Equal(input, result.Events);
            Assert.Equal(3, result.LogicalEvents.Count());
            Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
            Assert.Equal(2, result.Diagnostics.MirrorCandidatesHeldCount);
            AssertDiagnostics(result.Diagnostics);
        }
    }

    [Fact]
    public void Unique_proof_is_not_blocked_by_a_failed_nearby_comparison()
    {
        var events = ParseCanonical(TransfusionReceived, TransfusionDelivered, TransfusionReceived)
            .Select(item => WithChannel(item, item.Family == CombatEventFamily.HealReceived ? "TestReceipt" : "TestDelivery"))
            .ToArray();
        events[2] = events[2] with { PowerName = "Other" };
        var dedup = new Deduplicator(MirrorCompatibilityPolicy.ForTests(TestHealRule()));
        foreach (var input in new[] { events, events.Reverse().ToArray() })
        {
            var result = dedup.Deduplicate(input);
            Assert.Equal(2, result.LogicalEvents.Count());
            Assert.Equal(1, result.Diagnostics.MirrorCollapsedCount);
            Assert.Equal(1, result.Diagnostics.MirrorCandidatesHeldCount);
            Assert.Equal(events[0].Provenance.ParserSequence, Assert.Single(result.Events, item => item.DuplicateOf is not null).DuplicateOf!.ParserSequence);
            AssertDiagnostics(result.Diagnostics);
        }
    }

    private static void AssertDiagnostics(DedupDiagnostics diagnostics)
    {
        Assert.Equal(diagnostics.MirrorCollapsedCount, diagnostics.CollapsedByRule.Values.Sum());
        Assert.Equal(diagnostics.MirrorCandidatesHeldCount, diagnostics.HeldByRule.Values.Sum());
        Assert.Equal(2 * diagnostics.MirrorCollapsedCount, diagnostics.CollapsedByFamily.Values.Sum());
        Assert.Equal(2 * diagnostics.MirrorCandidatesHeldCount, diagnostics.HeldByFamily.Values.Sum());
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

    private static IReadOnlyList<CanonicalCombatEvent> LogicalMagnitudes(DedupResult result) =>
        result.LogicalEvents.ToArray();

    private static IReadOnlyList<CanonicalCombatEvent> WithComplementaryHealChannels(
        IReadOnlyList<CanonicalCombatEvent> events)
    {
        return
        [
            WithChannel(events[0], events[0].Family == CombatEventFamily.HealReceived ? "TestReceipt" : "TestDelivery"),
            WithChannel(events[1], events[1].Family == CombatEventFamily.HealDealt ? "TestDelivery" : "TestReceipt")
        ];
    }

    private static CanonicalCombatEvent WithChannel(CanonicalCombatEvent canonical, string channel) =>
        canonical with
        {
            SourceChannel = channel,
            MirrorClass = canonical.MirrorClass with { SourceChannel = channel },
            Provenance = canonical.Provenance with { SourceChannel = channel }
        };

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

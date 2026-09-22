using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Replay;

namespace CoHAnalytics.Tests.Services;

public sealed class Slice2CanonicalCombatEventTests
{
    private const string OverlappingUnnormalizableDamageThenDefeat =
        "2026-08-04 12:00:00 You hit A has defeated B for 1.234 points of Fire damage.";

    [Fact]
    public void Canonical_adapter_matches_frozen_core_and_accuracy_replay_oracles()
    {
        AssertCanonicalAdapterMatchesFrozenReplay(
            ReplayTestPaths.Fixture("combat-core-grammar.log"),
            new DateOnly(2026, 8, 4),
            CombatEventReplayOracleTests.CreateExpectedEvents);
        AssertCanonicalAdapterMatchesFrozenReplay(
            ReplayTestPaths.Fixture("combat-accuracy-grammar.log"),
            new DateOnly(2026, 8, 6),
            CombatAccuracyReplayOracleTests.CreateExpectedEvents);
    }

    [Theory]
    [MemberData(nameof(GrammarCoverageData))]
    public void Canonical_adapter_matches_parser_field_for_field(
        string line,
        CombatGrammarId expectedGrammar)
    {
        var input = CombatEventParserTestSupport.Classify(line);
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(input, out var parsed));
        Assert.Equal(expectedGrammar, parsed.GrammarId);
        Assert.True(TryNormalizeFirstSuccess(input, out var canonical));
        Assert.Equal(expectedGrammar, canonical.GrammarId);
        CombatEventParserTestSupport.AssertEquivalent(parsed, CanonicalToLegacyAdapter.ToLegacy(canonical));
    }

    [Fact]
    public void Every_legacy_mapped_CombatGrammarId_has_canonical_to_legacy_coverage()
    {
        var covered = GrammarCoverageCases.Select(row => row.GrammarId).ToArray();
        Assert.Equal(covered.Length, covered.Distinct().Count());
        CombatGrammarId[] legacyMapped =
        [
            CombatGrammarId.Dmg01YouHitWithPower,
            CombatGrammarId.Dmg02YouHitWithoutPower,
            CombatGrammarId.Dmg03SourceHitsYouWithPower,
            CombatGrammarId.Dmg04SourceHitsYouWithoutPower,
            CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower,
            CombatGrammarId.Dmg06SourceHitsYouWithTheirPower,
            CombatGrammarId.Heal01YouHealTarget,
            CombatGrammarId.Heal02YouHealYourself,
            CombatGrammarId.Heal03SourceHealsYou,
            CombatGrammarId.Heal04YouHealTargetHealthPoints,
            CombatGrammarId.Heal05SourceHealsYouWithTheirHealthPoints,
            CombatGrammarId.Act01YouActivate,
            CombatGrammarId.Act02YouActivatedThePower,
            CombatGrammarId.Def01YouHaveDefeated,
            CombatGrammarId.Def02OtherPlayerDefeated,
            CombatGrammarId.Acc01RolledHit,
            CombatGrammarId.Acc02RolledMiss,
            CombatGrammarId.Acc03ForcedHit,
            CombatGrammarId.Acc04Autohit
        ];
        Assert.Equal(legacyMapped.Order().ToArray(), covered.Order().ToArray());
    }

    [Fact]
    public void Provenance_is_copied_exactly_from_the_input_ParserEvent()
    {
        var contextId = MonitoringContextId.CreateNew();
        var input = CombatEventParserTestSupport.Classify(
            "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
            sequence: 42,
            contextId: contextId);
        input = input with
        {
            BindingGeneration = 9,
            SourceByteStart = 100,
            SourceByteEnd = 188
        };

        Assert.True(TryNormalizeFirstSuccess(input, out var canonical));
        var provenance = canonical.Provenance;
        Assert.Equal(contextId, provenance.ContextId);
        Assert.Equal(input.SourceId.Value, provenance.SourceId);
        Assert.Equal(input.SourceId.AccountStableId, provenance.AccountStableId);
        Assert.Equal(input.SourceSegmentId.Value, provenance.SourceSegmentId);
        Assert.Equal(9, provenance.BindingGeneration);
        Assert.Equal(42, provenance.ParserSequence);
        Assert.Equal(42, canonical.Sequence);
        Assert.Equal(100, provenance.ByteStart);
        Assert.Equal(188, provenance.ByteEnd);
        Assert.Equal(input.ObservedAt, provenance.ObservedAt);
        Assert.Equal(input.ObservedAt, canonical.ObservedAt);
        Assert.Equal(input.SourceTimestamp, provenance.SourceTimestamp);
        Assert.Equal(input.SourceTimestamp, canonical.SourceTimestamp);
        Assert.Equal(input.ClassificationRuleId, provenance.ClassificationRuleId);
        Assert.Equal(EventProvenance.CurrentGrammarSetVersion, provenance.GrammarSetVersion);
        Assert.Null(provenance.SourceChannel);
        Assert.Null(canonical.SourceChannel);
        Assert.Null(canonical.DuplicateOf);
        Assert.Equal(EventFacets.DamageDealt, canonical.Facets);
        Assert.Equal(ActorType.Self, canonical.Actor.Type);
        Assert.Equal(ActorType.Unknown, canonical.Target!.Type);
        Assert.Equal("Lusca", canonical.Target.DisplayName);
    }

    [Fact]
    public void Actual_channel_is_copied_into_parser_envelope_and_canonical_provenance()
    {
        var npc = CombatEventParserTestSupport.Classify(
            "2026-08-04 12:00:00 [NPC] Contact: You hit Training Dummy for 12 points of Fire damage.");
        Assert.Equal("NPC", npc.SourceChannel);
        Assert.Equal("NPC", EventProvenance.FromParserEvent(npc).SourceChannel);
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(npc, out _));

        var combat = CombatEventParserTestSupport.Classify(
            "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.");
        Assert.Null(combat.SourceChannel);
        var withChannel = combat with { SourceChannel = "Team" };
        Assert.True(TryNormalizeFirstSuccess(withChannel, out var canonical));
        Assert.Equal("Team", canonical.Provenance.SourceChannel);
        Assert.Equal("Team", canonical.SourceChannel);
        Assert.Equal("Team", canonical.MirrorClass.SourceChannel);
        Assert.Null(canonical.DuplicateOf);
    }

    [Fact]
    public void Absent_channel_stays_null_on_bare_combat_lines()
    {
        var input = CombatEventParserTestSupport.Classify(
            "2026-08-04 12:00:00 You activate Fire Cages.");
        Assert.Null(input.SourceChannel);
        Assert.True(TryNormalizeFirstSuccess(input, out var canonical));
        Assert.Null(canonical.Provenance.SourceChannel);
        Assert.Null(canonical.SourceChannel);
        Assert.Null(canonical.MirrorClass.SourceChannel);
    }

    [Fact]
    public void Parser_sequence_is_scoped_by_source_and_segment_and_is_not_a_global_key()
    {
        const string line = "2026-08-04 12:00:00 You activate Fire Cages.";
        var first = CombatEventParserTestSupport.Classify(line, sequence: 7);
        var secondRaw = new ParserRawEvent
        {
            ContextId = MonitoringContextId.CreateNew(),
            SourceId = LogSourceId.Create(
                "acct-2",
                "acct-2",
                "D:\\other\\chatlog.txt",
                new DateOnly(2026, 8, 4)),
            SourceSegmentId = ParserSourceSegmentId.CreateNew(),
            BindingGeneration = 3,
            SourceTransitionKind = MonitoringSourceTransitionKind.SourceAssigned,
            Sequence = 7,
            ObservedAt = first.ObservedAt,
            RawLine = line,
            SourceByteStart = 50,
            SourceByteEnd = line.Length + 50,
            LineStatus = ParserLineStatus.Complete
        };
        var second = CombatEventParserTestSupport.Classifier.Classify(secondRaw);

        Assert.True(TryNormalizeFirstSuccess(first, out var firstCanonical));
        Assert.True(TryNormalizeFirstSuccess(second, out var secondCanonical));
        Assert.Equal(7, firstCanonical.Provenance.ParserSequence);
        Assert.Equal(7, secondCanonical.Provenance.ParserSequence);
        Assert.NotEqual(firstCanonical.Provenance.SourceId, secondCanonical.Provenance.SourceId);
        Assert.NotEqual(firstCanonical.Provenance.SourceSegmentId, secondCanonical.Provenance.SourceSegmentId);
        Assert.NotEqual(firstCanonical.Provenance.BindingGeneration, secondCanonical.Provenance.BindingGeneration);
        Assert.NotEqual(firstCanonical.Provenance, secondCanonical.Provenance);
        Assert.Null(firstCanonical.DuplicateOf);
        Assert.Null(secondCanonical.DuplicateOf);
    }

    [Fact]
    public void Ordered_fallback_still_emits_frozen_Def02_legacy_event()
    {
        var contextId = MonitoringContextId.CreateNew();
        var input = CombatEventParserTestSupport.Classify(
            OverlappingUnnormalizableDamageThenDefeat,
            sequence: 9,
            contextId: contextId);
        Assert.True(ParserLineEnvelope.TryGetBody(input.RawLine, input.SourceId.LogDate, out var body));
        var matches = GrammarMatcher.EnumerateMatches(body).ToArray();
        Assert.Equal(CombatGrammarId.Dmg02YouHitWithoutPower, matches[0].GrammarId);
        Assert.False(Normalizer.TryNormalize(matches[0], input, out _));
        Assert.True(Normalizer.TryNormalize(matches[1], input, out var canonical));
        Assert.Equal(CombatGrammarId.Def02OtherPlayerDefeated, canonical.GrammarId);
        Assert.Equal(CombatEventFamily.Defeat, canonical.Family);

        var expected = new CombatEvent
        {
            ContextId = contextId,
            ParserSequence = 9,
            ObservedAt = new DateTimeOffset(2026, 8, 4, 12, 0, 8, TimeSpan.Zero),
            SourceTimestamp = new DateTime(2026, 8, 4, 12, 0, 0),
            Kind = CombatEventKind.Defeat,
            GrammarId = CombatGrammarId.Def02OtherPlayerDefeated,
            ActorRole = CombatActorRole.Other,
            TargetName = "B for 1.234 points of Fire damage",
            SourceName = "You hit A",
            Amount = CombatScaledAmount.Zero
        };
        CombatEventParserTestSupport.AssertEquivalent(expected, CanonicalToLegacyAdapter.ToLegacy(canonical));
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(input, out var parsed));
        CombatEventParserTestSupport.AssertEquivalent(expected, parsed);
        Assert.False(CombatEventParser.IsCombatShapedUnparsed(input));
    }

    [Fact]
    public void Delivery_flags_and_accuracy_fields_round_trip_through_the_adapter()
    {
        Assert.True(TryNormalizeFirstSuccess(
            CombatEventParserTestSupport.Classify(
                "2026-08-04 12:00:02 You hit Lusca with your Fire Cages for 10.36 points of Fire damage over time (CONTAINMENT)."),
            out var containment));
        Assert.True(containment.Delivery.HasFlag(DeliveryFlags.DoT));
        Assert.True(containment.Delivery.HasFlag(DeliveryFlags.Containment));
        Assert.Equal("CONTAINMENT", containment.EffectSuffix);
        var legacyContainment = CanonicalToLegacyAdapter.ToLegacy(containment);
        Assert.True(legacyContainment.IsOverTime);
        Assert.Equal("CONTAINMENT", legacyContainment.EffectSuffix);

        Assert.True(TryNormalizeFirstSuccess(
            CombatEventParserTestSupport.Classify(
                "2026-08-04 12:00:05 Lieutenant Skull critically hits you with Sniper Rifle for 55.0 points of Lethal damage."),
            out var critical));
        Assert.True(critical.Delivery.HasFlag(DeliveryFlags.Critical));
        Assert.Equal(CombatActorRole.Other, CanonicalToLegacyAdapter.ToLegacy(critical).ActorRole);

        Assert.True(TryNormalizeFirstSuccess(
            CombatEventParserTestSupport.Classify(
                "2026-08-06 12:00:00 HIT Rikti Pylon! Your Flashfire power had a 95.00% chance to hit, you rolled a 51.51."),
            out var rolled));
        var legacyRolled = CanonicalToLegacyAdapter.ToLegacy(rolled);
        Assert.True(legacyRolled.WasRolled);
        Assert.False(legacyRolled.WasForced);
        Assert.False(legacyRolled.IsAutohit);
        Assert.Equal(9500, legacyRolled.DisplayedChanceHundredths);
        Assert.Equal(5151, legacyRolled.RollHundredths);

        Assert.True(TryNormalizeFirstSuccess(
            CombatEventParserTestSupport.Classify(
                "2026-08-06 12:00:04 HIT Lieutenant Skull! Your Fire Bolt power was forced to hit by streakbreaker."),
            out var forced));
        var legacyForced = CanonicalToLegacyAdapter.ToLegacy(forced);
        Assert.True(legacyForced.WasForced);
        Assert.False(legacyForced.WasRolled);
        Assert.False(legacyForced.IsAutohit);

        Assert.True(TryNormalizeFirstSuccess(
            CombatEventParserTestSupport.Classify(
                "2026-08-06 12:00:05 HIT Training Dummy! Your Siphon Power power is autohit."),
            out var autohit));
        var legacyAutohit = CanonicalToLegacyAdapter.ToLegacy(autohit);
        Assert.True(legacyAutohit.IsAutohit);
        Assert.False(legacyAutohit.WasRolled);
        Assert.False(legacyAutohit.WasForced);
    }

    [Fact]
    public void Companion_miss_does_not_emit_legacy_or_combat_shaped_unparsed()
    {
        var miss = CombatEventParserTestSupport.Classify("2026-08-06 12:00:03 Fire Cages missed!");
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(miss, out _));
        Assert.False(CombatEventParser.IsCombatShapedUnparsed(miss));
        Assert.True(ParserLineEnvelope.TryGetBody(miss.RawLine, miss.SourceId.LogDate, out var missBody));
        Assert.True(TryNormalizeFirstSuccess(miss, out var canonicalMiss));
        Assert.Equal(CombatGrammarId.Cmp01CompanionMissSummary, canonicalMiss.GrammarId);
        Assert.False(CanonicalToLegacyAdapter.TryToLegacy(canonicalMiss, out _));
        Assert.Equal(CombatGrammarId.Cmp01CompanionMissSummary, GrammarMatcher.EnumerateMatches(missBody).Single().GrammarId);

        var unparsed = CombatEventParserTestSupport.Classify(
            "2026-08-04 12:00:00 You take 12 points of Toxic damage from Poison Gas.");
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(unparsed, out _));
        Assert.True(CombatEventParser.IsCombatShapedUnparsed(unparsed));
        Assert.False(TryNormalizeFirstSuccess(unparsed, out _));
    }

    public static TheoryData<string, CombatGrammarId> GrammarCoverageData
    {
        get
        {
            var data = new TheoryData<string, CombatGrammarId>();
            foreach (var row in GrammarCoverageCases)
            {
                data.Add(row.Line, row.GrammarId);
            }

            return data;
        }
    }

    private static readonly GrammarCoverageCase[] GrammarCoverageCases =
    [
        new("2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
            CombatGrammarId.Dmg01YouHitWithPower),
        new("2026-08-04 12:00:00 You hit Training Dummy for 12 points of Fire damage.",
            CombatGrammarId.Dmg02YouHitWithoutPower),
        new("2026-08-04 12:00:00 Crey Thorn Mook hits you with Bone Shard for 22.15 points of Lethal damage.",
            CombatGrammarId.Dmg03SourceHitsYouWithPower),
        new("2026-08-04 12:00:00 Fictional Target hits you for 15 points of lethal damage.",
            CombatGrammarId.Dmg04SourceHitsYouWithoutPower),
        new("2026-08-04 12:00:00 Lieutenant Skull critically hits you with Sniper Rifle for 55.0 points of Lethal damage.",
            CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower),
        new("2026-08-04 12:00:00 Example Villain hits you with their Fire Ball for 12 points of Fire damage.",
            CombatGrammarId.Dmg06SourceHitsYouWithTheirPower),
        new("2026-08-04 12:00:00 You heal Example Ally for 78.50 hit points with Healing Aura.",
            CombatGrammarId.Heal01YouHealTarget),
        new("2026-08-04 12:00:00 You heal yourself for 15.00 hit points with Regeneration.",
            CombatGrammarId.Heal02YouHealYourself),
        new("2026-08-04 12:00:00 Example Medic heals you for 42.25 hit points with Aid.",
            CombatGrammarId.Heal03SourceHealsYou),
        new("2026-08-04 12:00:00 You heal Imp with Transfusion for 421.34 health points.",
            CombatGrammarId.Heal04YouHealTargetHealthPoints),
        new("2026-08-04 12:00:00 Hero_A heals you with their Panacea: Chance for +Hit Points/Endurance for 78.93 health points.",
            CombatGrammarId.Heal05SourceHealsYouWithTheirHealthPoints),
        new("2026-08-04 12:00:00 You activate Fire Cages.",
            CombatGrammarId.Act01YouActivate),
        new("2026-08-04 12:00:00 You activated the Fire Cages power.",
            CombatGrammarId.Act02YouActivatedThePower),
        new("2026-08-04 12:00:00 You have defeated Lusca",
            CombatGrammarId.Def01YouHaveDefeated),
        new("2026-08-04 12:00:00 Psiche has defeated Prototype Oscillator",
            CombatGrammarId.Def02OtherPlayerDefeated),
        new("2026-08-06 12:00:00 HIT Rikti Pylon! Your Flashfire power had a 95.00% chance to hit, you rolled a 51.51.",
            CombatGrammarId.Acc01RolledHit),
        new("2026-08-06 12:00:00 MISSED Spirit!! Your Fire Cages power had a 95.00% chance to hit, you rolled a 97.54.",
            CombatGrammarId.Acc02RolledMiss),
        new("2026-08-06 12:00:00 HIT Lieutenant Skull! Your Fire Bolt power was forced to hit by streakbreaker.",
            CombatGrammarId.Acc03ForcedHit),
        new("2026-08-06 12:00:00 HIT Training Dummy! Your Siphon Power power is autohit.",
            CombatGrammarId.Acc04Autohit)
    ];

    private static void AssertCanonicalAdapterMatchesFrozenReplay(
        string fixturePath,
        DateOnly logDate,
        Func<MonitoringContextId, IReadOnlyList<CombatEvent>> createExpected)
    {
        var lines = File.ReadAllLines(fixturePath);
        var contextId = MonitoringContextId.CreateNew();
        var expected = createExpected(contextId);
        var parsed = CombatEventParserTestSupport.ParseFixtureLines(lines, logDate, contextId);
        Assert.Equal(expected.Count, parsed.Count);

        var adapted = new List<CombatEvent>();
        var sequence = 1L;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var input = CombatEventParserTestSupport.Classify(line, sequence, logDate, contextId);
        if (TryNormalizeFirstSuccess(input, out var canonical)
            && CanonicalToLegacyAdapter.TryToLegacy(canonical, out var adaptedEvent))
        {
            adapted.Add(adaptedEvent);
        }

            sequence++;
        }

        Assert.Equal(expected.Count, adapted.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            CombatEventParserTestSupport.AssertEquivalent(expected[index], parsed[index]);
            CombatEventParserTestSupport.AssertEquivalent(expected[index], adapted[index]);
        }
    }

    private static bool TryNormalizeFirstSuccess(ParserEvent input, out CanonicalCombatEvent canonicalEvent)
    {
        canonicalEvent = null!;
        if (!ParserLineEnvelope.TryGetBody(input.RawLine, input.SourceId.LogDate, out var body))
        {
            return false;
        }

        foreach (var match in GrammarMatcher.EnumerateMatches(body))
        {
            if (Normalizer.TryNormalize(match, input, out canonicalEvent))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record GrammarCoverageCase(string Line, CombatGrammarId GrammarId);
}

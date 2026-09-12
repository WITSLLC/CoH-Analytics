using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Replay;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Slice 1 legacy equivalence oracle: <see cref="CombatEventParser"/> output is asserted against
/// frozen expected events and diagnostics, not against a reconstructed matcher/normalizer loop.
/// </summary>
public sealed class Slice1LegacyEquivalenceOracleTests
{
    private const string OverlappingUnnormalizableDamageThenDefeat =
        "2026-08-04 12:00:00 You hit A has defeated B for 1.234 points of Fire damage.";

    [Fact]
    public void Parser_matches_frozen_core_and_accuracy_replay_oracles()
    {
        AssertParserMatchesFrozenReplay(
            ReplayTestPaths.Fixture("combat-core-grammar.log"),
            new DateOnly(2026, 8, 4),
            CombatEventReplayOracleTests.CreateExpectedEvents);
        AssertParserMatchesFrozenReplay(
            ReplayTestPaths.Fixture("combat-accuracy-grammar.log"),
            new DateOnly(2026, 8, 6),
            CombatAccuracyReplayOracleTests.CreateExpectedEvents);
    }

    [Fact]
    public void Parser_walks_every_sanitized_max_channel_fixture_line()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Combat", "max-channel-2026-09-12.tsv");
        foreach (var rawLine in File.ReadLines(path).Select(line => line[(line.IndexOf('\t') + 1)..]))
        {
            var input = CombatEventParserTestSupport.Classify(rawLine, logDate: new DateOnly(2026, 9, 12));
            var parsed = CombatEventParserTestSupport.Parser.TryParse(input, out var combatEvent);
            var unparsed = CombatEventParser.IsCombatShapedUnparsed(input);
            if (parsed)
            {
                Assert.NotNull(combatEvent);
                Assert.False(unparsed);
            }
            else
            {
                Assert.Null(combatEvent);
            }
        }
    }

    [Theory]
    [InlineData("2026-08-04 12:00:00 You take 12 points of Toxic damage from Poison Gas.", true)]
    [InlineData("2026-08-04 12:00:00 You hit Training Dummy with your Fire Ball for bad points of Fire damage.", true)]
    [InlineData("2026-08-04 12:00:00 Fire Cages missed!", false)]
    [InlineData("2026-08-04 12:00:00 Imp: You hit Training Dummy with your Fire Ball for 12 points of Fire damage.", false)]
    [InlineData("2026-08-04 12:00:00 utterly unknown future text", false)]
    [InlineData("2026-99-04 12:00:00 You hit Training Dummy for 12 points of Fire damage.", false)]
    [InlineData("2026-08-04 12:00:00 The briefing has defeated morale among the team.", true)]
    public void Failures_and_combat_shaped_unparsed_match_legacy_behavior(
        string line, bool expectedCombatShapedUnparsed)
    {
        var input = CombatEventParserTestSupport.Classify(line);
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out var rejected));
        Assert.Null(rejected);
        Assert.Equal(expectedCombatShapedUnparsed, CombatEventParser.IsCombatShapedUnparsed(input));
    }

    [Fact]
    public void Companion_miss_summary_is_suppressed_at_grammar_layer_and_does_not_parse()
    {
        const string line = "2026-08-06 12:00:03 Fire Cages missed!";
        var input = CombatEventParserTestSupport.Classify(line);
        Assert.True(ParserLineEnvelope.TryGetBody(input.RawLine, input.SourceId.LogDate, out var body));
        Assert.True(GrammarMatcher.IsCompanionMissSummary(body));
        Assert.False(GrammarMatcher.TryMatch(body, out _));
        Assert.Empty(GrammarMatcher.EnumerateMatches(body));
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out _));
        Assert.False(CombatEventParser.IsCombatShapedUnparsed(input));
    }

    [Fact]
    public void Precedence_self_heal_and_critical_remain_unchanged_through_the_split()
    {
        var healInput = CombatEventParserTestSupport.Classify(
            "2026-08-04 12:00:00 You heal yourself for 15 hit points with Regeneration.");
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(healInput, out var heal));
        CombatEventParserTestSupport.AssertEquivalent(
            new CombatEvent
            {
                ContextId = healInput.ContextId,
                ParserSequence = healInput.Sequence,
                ObservedAt = healInput.ObservedAt,
                SourceTimestamp = healInput.SourceTimestamp,
                Kind = CombatEventKind.HealingDealt,
                GrammarId = CombatGrammarId.Heal02YouHealYourself,
                ActorRole = CombatActorRole.Self,
                TargetName = "yourself",
                PowerName = "Regeneration",
                Amount = new CombatScaledAmount(1500)
            },
            heal);

        var criticalInput = CombatEventParserTestSupport.Classify(
            "2026-08-04 12:00:01 Crey Thorn Mook critically hits you with Bone Shard for 22 points of Lethal damage.");
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(criticalInput, out var critical));
        CombatEventParserTestSupport.AssertEquivalent(
            new CombatEvent
            {
                ContextId = criticalInput.ContextId,
                ParserSequence = criticalInput.Sequence,
                ObservedAt = criticalInput.ObservedAt,
                SourceTimestamp = criticalInput.SourceTimestamp,
                Kind = CombatEventKind.DamageReceived,
                GrammarId = CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower,
                ActorRole = CombatActorRole.Other,
                SourceName = "Crey Thorn Mook",
                PowerName = "Bone Shard",
                Amount = new CombatScaledAmount(2200),
                DamageType = "Lethal"
            },
            critical);
    }

    [Fact]
    public void Autohit_and_streakbreaker_normalize_the_same_flags_as_the_parser()
    {
        var autohitInput = CombatEventParserTestSupport.Classify(
            "2026-08-06 12:00:05 HIT Training Dummy! Your Siphon Power power is autohit.");
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(autohitInput, out var autohit));
        CombatEventParserTestSupport.AssertEquivalent(
            new CombatEvent
            {
                ContextId = autohitInput.ContextId,
                ParserSequence = autohitInput.Sequence,
                ObservedAt = autohitInput.ObservedAt,
                SourceTimestamp = autohitInput.SourceTimestamp,
                Kind = CombatEventKind.AttackResolution,
                GrammarId = CombatGrammarId.Acc04Autohit,
                ActorRole = CombatActorRole.Self,
                TargetName = "Training Dummy",
                PowerName = "Siphon Power",
                AttackOutcome = CombatAttackOutcome.Hit,
                WasRolled = false,
                WasForced = false,
                IsAutohit = true
            },
            autohit);

        var forcedInput = CombatEventParserTestSupport.Classify(
            "2026-08-06 12:00:04 HIT Lieutenant Skull! Your Fire Bolt power was forced to hit by streakbreaker.");
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(forcedInput, out var forced));
        CombatEventParserTestSupport.AssertEquivalent(
            new CombatEvent
            {
                ContextId = forcedInput.ContextId,
                ParserSequence = forcedInput.Sequence,
                ObservedAt = forcedInput.ObservedAt,
                SourceTimestamp = forcedInput.SourceTimestamp,
                Kind = CombatEventKind.AttackResolution,
                GrammarId = CombatGrammarId.Acc03ForcedHit,
                ActorRole = CombatActorRole.Self,
                TargetName = "Lieutenant Skull",
                PowerName = "Fire Bolt",
                AttackOutcome = CombatAttackOutcome.Hit,
                WasRolled = false,
                WasForced = true,
                IsAutohit = false
            },
            forced);
    }

    [Fact]
    public void Bracket_and_full_timestamps_and_absent_source_timestamp_survive_normalization()
    {
        var contextId = MonitoringContextId.CreateNew();
        var bracket = CombatEventParserTestSupport.Classify(
            "[12:05] You hit Training Dummy with your Fire Ball for 12.34 points of fire damage.",
            sequence: 17,
            contextId: contextId);
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(bracket, out var parsedBracket));
        CombatEventParserTestSupport.AssertEquivalent(
            new CombatEvent
            {
                ContextId = contextId,
                ParserSequence = 17,
                ObservedAt = new DateTimeOffset(2026, 8, 4, 12, 0, 16, TimeSpan.Zero),
                SourceTimestamp = new DateTime(2026, 8, 4, 12, 5, 0, DateTimeKind.Unspecified),
                Kind = CombatEventKind.DamageDealt,
                GrammarId = CombatGrammarId.Dmg01YouHitWithPower,
                ActorRole = CombatActorRole.Self,
                TargetName = "Training Dummy",
                PowerName = "Fire Ball",
                Amount = new CombatScaledAmount(1234),
                DamageType = "Fire"
            },
            parsedBracket);

        var untimestamped = CombatEventParserTestSupport.Classify("You activate Fire Cages.", sequence: 4);
        Assert.Equal(ParserEventKind.Unknown, untimestamped.EventKind);
        Assert.Null(untimestamped.SourceTimestamp);
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(untimestamped, out var parsedActivate));
        CombatEventParserTestSupport.AssertEquivalent(
            new CombatEvent
            {
                ContextId = untimestamped.ContextId,
                ParserSequence = 4,
                ObservedAt = untimestamped.ObservedAt,
                SourceTimestamp = null,
                Kind = CombatEventKind.PowerActivation,
                GrammarId = CombatGrammarId.Act01YouActivate,
                ActorRole = CombatActorRole.Self,
                PowerName = "Fire Cages",
                Amount = CombatScaledAmount.Zero
            },
            parsedActivate);
    }

    [Fact]
    public void PotentialIdentityEvidence_is_not_admitted_even_when_the_inner_shape_grammar_matches()
    {
        const string line =
            "2026-08-04 12:00:00 Example Villain hits you with their Fire Ball for 12 points of Fire damage.";
        var input = CombatEventParserTestSupport.Classify(line);
        Assert.Equal(ParserEventKind.PotentialIdentityEvidence, input.EventKind);
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out _));
        Assert.False(CombatEventParser.IsCombatShapedUnparsed(input));

        Assert.True(ParserLineEnvelope.TryGetBody(input.RawLine, input.SourceId.LogDate, out var body));
        Assert.False(CombatEventParser.IsCombatCandidate(input.EventKind, body));
        Assert.True(GrammarMatcher.TryMatch(body, out var match));
        Assert.Equal(CombatGrammarId.Dmg03SourceHitsYouWithPower, match.GrammarId);
        Assert.Equal("their Fire Ball", match.Capture("power"));
        Assert.True(Normalizer.TryNormalize(match, input, out _));
    }

    [Fact]
    public void Defeat_name_filter_is_a_normalizer_rejection_not_a_missing_grammar()
    {
        const string line = "2026-08-04 12:00:14 The briefing has defeated morale among the team.";
        var input = CombatEventParserTestSupport.Classify(line);
        Assert.True(ParserLineEnvelope.TryGetBody(input.RawLine, input.SourceId.LogDate, out var body));
        Assert.True(GrammarMatcher.TryMatch(body, out var match));
        Assert.Equal(CombatGrammarId.Def02OtherPlayerDefeated, match.GrammarId);
        Assert.False(Normalizer.TryNormalize(match, input, out _));
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out _));
        Assert.True(CombatEventParser.IsCombatShapedUnparsed(input));
    }

    [Fact]
    public void Unnormalizable_earlier_damage_grammar_falls_back_to_later_defeat_event()
    {
        var contextId = MonitoringContextId.CreateNew();
        var input = CombatEventParserTestSupport.Classify(
            OverlappingUnnormalizableDamageThenDefeat,
            sequence: 9,
            contextId: contextId);
        Assert.True(ParserLineEnvelope.TryGetBody(input.RawLine, input.SourceId.LogDate, out var body));

        var matches = GrammarMatcher.EnumerateMatches(body).ToArray();
        Assert.Equal(
            [CombatGrammarId.Dmg02YouHitWithoutPower, CombatGrammarId.Def02OtherPlayerDefeated],
            matches.Select(match => match.GrammarId).ToArray());
        Assert.Equal("1.234", matches[0].Capture("amount"));
        Assert.False(Normalizer.TryNormalize(matches[0], input, out _));
        Assert.True(Normalizer.TryNormalize(matches[1], input, out var normalizedDefeat));

        Assert.True(CombatEventParserTestSupport.Parser.TryParse(input, out var actual));
        Assert.False(CombatEventParser.IsCombatShapedUnparsed(input));
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
        CombatEventParserTestSupport.AssertEquivalent(expected, actual);
        CombatEventParserTestSupport.AssertEquivalent(expected, normalizedDefeat);
    }

    [Fact]
    public void Unnormalizable_overlapping_damage_grammars_remain_unparsed_when_no_later_grammar_succeeds()
    {
        const string line = "2026-08-04 12:00:00 You hit Training Dummy with your Fire Ball for 1.234 points of Fire damage.";
        var input = CombatEventParserTestSupport.Classify(line);
        Assert.True(ParserLineEnvelope.TryGetBody(input.RawLine, input.SourceId.LogDate, out var body));

        var matches = GrammarMatcher.EnumerateMatches(body).ToArray();
        Assert.Equal(
            [CombatGrammarId.Dmg01YouHitWithPower, CombatGrammarId.Dmg02YouHitWithoutPower],
            matches.Select(match => match.GrammarId).ToArray());
        Assert.All(matches, match => Assert.False(Normalizer.TryNormalize(match, input, out _)));

        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out var rejected));
        Assert.Null(rejected);
        Assert.True(CombatEventParser.IsCombatShapedUnparsed(input));
    }

    private static void AssertParserMatchesFrozenReplay(
        string fixturePath,
        DateOnly logDate,
        Func<MonitoringContextId, IReadOnlyList<CombatEvent>> createExpected)
    {
        var lines = File.ReadAllLines(fixturePath);
        var contextId = MonitoringContextId.CreateNew();
        var actual = CombatEventParserTestSupport.ParseFixtureLines(lines, logDate, contextId);
        var expected = createExpected(contextId);
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            CombatEventParserTestSupport.AssertEquivalent(expected[index], actual[index]);
        }
    }
}

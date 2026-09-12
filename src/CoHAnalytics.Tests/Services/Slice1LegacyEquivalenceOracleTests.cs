using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Replay;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Slice 1 legacy equivalence oracle: GrammarMatcher + Normalizer must equal CombatEventParser
/// for successes, failures, unparsed diagnostics, companion-miss, precedence, autohit, and timestamps.
/// </summary>
public sealed class Slice1LegacyEquivalenceOracleTests
{
    [Fact]
    public void Split_path_equals_parser_for_core_and_accuracy_replay_fixtures()
    {
        AssertSplitEqualsParser(ReplayTestPaths.Fixture("combat-core-grammar.log"), new DateOnly(2026, 8, 4));
        AssertSplitEqualsParser(ReplayTestPaths.Fixture("combat-accuracy-grammar.log"), new DateOnly(2026, 8, 6));
    }

    [Fact]
    public void Split_path_equals_parser_for_sanitized_max_channel_fixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Combat", "max-channel-2026-09-12.tsv");
        foreach (var rawLine in File.ReadLines(path).Select(line => line[(line.IndexOf('\t') + 1)..]))
        {
            AssertSplitEqualsParserLine(rawLine, new DateOnly(2026, 9, 12));
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
        AssertSplitEqualsParserLine(line, new DateOnly(2026, 8, 4));
    }

    [Fact]
    public void Companion_miss_summary_is_suppressed_at_grammar_layer_and_does_not_parse()
    {
        const string line = "2026-08-06 12:00:03 Fire Cages missed!";
        var input = CombatEventParserTestSupport.Classify(line);
        Assert.True(ParserLineEnvelope.TryGetBody(input.RawLine, input.SourceId.LogDate, out var body));
        Assert.True(GrammarMatcher.IsCompanionMissSummary(body));
        Assert.False(GrammarMatcher.TryMatch(body, out _));
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out _));
        Assert.False(CombatEventParser.IsCombatShapedUnparsed(input));
    }

    [Fact]
    public void Precedence_self_heal_and_critical_remain_unchanged_through_the_split()
    {
        Assert.True(TrySplitParse(
            "2026-08-04 12:00:00 You heal yourself for 15 hit points with Regeneration.",
            out var heal,
            out var splitHeal));
        CombatEventParserTestSupport.AssertEquivalent(heal, splitHeal);
        Assert.Equal(CombatGrammarId.Heal02YouHealYourself, heal.GrammarId);
        Assert.Equal("yourself", heal.TargetName);

        Assert.True(TrySplitParse(
            "2026-08-04 12:00:01 Crey Thorn Mook critically hits you with Bone Shard for 22 points of Lethal damage.",
            out var critical,
            out var splitCritical));
        CombatEventParserTestSupport.AssertEquivalent(critical, splitCritical);
        Assert.Equal(CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower, critical.GrammarId);
    }

    [Fact]
    public void Autohit_and_streakbreaker_normalize_the_same_flags_as_the_parser()
    {
        Assert.True(TrySplitParse(
            "2026-08-06 12:00:05 HIT Training Dummy! Your Siphon Power power is autohit.",
            out var autohit,
            out var splitAutohit));
        CombatEventParserTestSupport.AssertEquivalent(autohit, splitAutohit);
        Assert.True(autohit.IsAutohit);
        Assert.False(autohit.WasRolled);
        Assert.Null(autohit.DisplayedChanceHundredths);

        Assert.True(TrySplitParse(
            "2026-08-06 12:00:04 HIT Lieutenant Skull! Your Fire Bolt power was forced to hit by streakbreaker.",
            out var forced,
            out var splitForced));
        CombatEventParserTestSupport.AssertEquivalent(forced, splitForced);
        Assert.True(forced.WasForced);
        Assert.False(forced.IsAutohit);
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
        Assert.True(TrySplitFromClassified(bracket, out var splitBracket));
        CombatEventParserTestSupport.AssertEquivalent(parsedBracket, splitBracket);
        Assert.Equal(new DateTime(2026, 8, 4, 12, 5, 0, DateTimeKind.Unspecified), parsedBracket.SourceTimestamp);
        Assert.Equal(new DateTimeOffset(2026, 8, 4, 12, 0, 16, TimeSpan.Zero), parsedBracket.ObservedAt);

        var untimestamped = CombatEventParserTestSupport.Classify("You activate Fire Cages.", sequence: 4);
        Assert.Null(untimestamped.SourceTimestamp);
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(untimestamped, out var parsedActivate));
        Assert.True(TrySplitFromClassified(untimestamped, out var splitActivate));
        CombatEventParserTestSupport.AssertEquivalent(parsedActivate, splitActivate);
        Assert.Null(parsedActivate.SourceTimestamp);
        Assert.Equal(untimestamped.ObservedAt, parsedActivate.ObservedAt);
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

    private static void AssertSplitEqualsParser(string fixturePath, DateOnly logDate)
    {
        foreach (var line in File.ReadAllLines(fixturePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            AssertSplitEqualsParserLine(line, logDate);
        }
    }

    private static void AssertSplitEqualsParserLine(string line, DateOnly logDate)
    {
        var input = CombatEventParserTestSupport.Classify(line, logDate: logDate);
        var parsed = CombatEventParserTestSupport.Parser.TryParse(input, out var parserEvent);
        var split = TrySplitFromClassified(input, out var splitEvent);
        Assert.Equal(parsed, split);
        if (parsed)
        {
            CombatEventParserTestSupport.AssertEquivalent(parserEvent, splitEvent);
        }
        else
        {
            Assert.Null(parserEvent);
            Assert.Null(splitEvent);
        }
    }

    private static bool TrySplitParse(string line, out CombatEvent parsed, out CombatEvent split)
    {
        var input = CombatEventParserTestSupport.Classify(line);
        var parserSucceeded = CombatEventParserTestSupport.Parser.TryParse(input, out parsed);
        var splitSucceeded = TrySplitFromClassified(input, out split);
        Assert.Equal(parserSucceeded, splitSucceeded);
        return parserSucceeded;
    }

    private static bool TrySplitFromClassified(ParserEvent input, out CombatEvent combatEvent)
    {
        combatEvent = null!;
        if (input.LineStatus != ParserLineStatus.Complete
            || input.EventKind is ParserEventKind.Malformed)
        {
            return false;
        }

        if (!ParserLineEnvelope.TryGetBody(input.RawLine, input.SourceId.LogDate, out var body))
        {
            return false;
        }

        if (!CombatEventParser.IsCombatCandidate(input.EventKind, body))
        {
            return false;
        }

        if (!GrammarMatcher.TryMatch(body, out var match))
        {
            return false;
        }

        return Normalizer.TryNormalize(match, input, out combatEvent);
    }
}

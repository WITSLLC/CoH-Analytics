using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

/// <summary>Locks current behavior for the later grammar/normalization split; these are not proposed new grammars.</summary>
public sealed class AnalyticsPreSliceParserCheckpointTests
{
    [Fact]
    public void Existing_golden_oracles_compare_every_legacy_CombatEvent_field()
    {
        // CombatEventReplayOracleTests and CombatAccuracyReplayOracleTests use
        // CombatEventParserTestSupport.AssertEquivalent. Guard its coverage as the model evolves.
        var expectedProperties = typeof(CombatEvent).GetProperties().Select(property => property.Name).Order().ToArray();
        var guardedProperties = new[]
        {
            nameof(CombatEvent.ContextId), nameof(CombatEvent.SessionId),
            nameof(CombatEvent.ParserSequence), nameof(CombatEvent.ObservedAt), nameof(CombatEvent.SourceTimestamp),
            nameof(CombatEvent.Kind), nameof(CombatEvent.GrammarId), nameof(CombatEvent.ActorRole),
            nameof(CombatEvent.TargetName), nameof(CombatEvent.SourceName), nameof(CombatEvent.PowerName),
            nameof(CombatEvent.Amount), nameof(CombatEvent.DamageType), nameof(CombatEvent.IsOverTime),
            nameof(CombatEvent.EffectSuffix), nameof(CombatEvent.AttackOutcome),
            nameof(CombatEvent.DisplayedChanceHundredths), nameof(CombatEvent.RollHundredths),
            nameof(CombatEvent.WasRolled), nameof(CombatEvent.WasForced), nameof(CombatEvent.IsAutohit)
        }.Order().ToArray();
        Assert.Equal(expectedProperties, guardedProperties);
    }

    [Fact]
    public void Bracket_timestamp_preserves_minute_precision_and_observation_time()
    {
        var contextId = MonitoringContextId.CreateNew();
        var input = CombatEventParserTestSupport.Classify(
            "[12:05] You hit Training Dummy with your Fire Ball for 12.34 points of fire damage.",
            sequence: 17, contextId: contextId);

        Assert.True(CombatEventParserTestSupport.Parser.TryParse(input, out var actual));
        var expected = new CombatEvent
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
        };
        CombatEventParserTestSupport.AssertEquivalent(expected, actual);
    }

    [Fact]
    public void Untimestamped_combat_keeps_null_source_timestamp()
    {
        var input = CombatEventParserTestSupport.Classify("You activate Fire Cages.", sequence: 4);
        Assert.Equal(ParserEventKind.Unknown, input.EventKind); // "You activate" is not a classifier system prefix.
        Assert.Null(input.SourceTimestamp);
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(input, out var actual));
        Assert.Equal(CombatGrammarId.Act01YouActivate, actual.GrammarId);
        Assert.Equal(input.ContextId, actual.ContextId);
        Assert.Equal(input.Sequence, actual.ParserSequence);
        Assert.Equal(input.ObservedAt, actual.ObservedAt);
        Assert.Null(actual.SourceTimestamp);
    }

    [Theory]
    [InlineData("2026-08-04 12:00:00 You take 12 points of Toxic damage from Poison Gas.", ParserEventKind.TimestampedLine, true)]
    [InlineData("2026-08-04 12:00:00 You hit Training Dummy with your Fire Ball for bad points of Fire damage.", ParserEventKind.SystemLine, true)]
    [InlineData("2026-08-04 12:00:00 Fire Cages missed!", ParserEventKind.TimestampedLine, false)]
    [InlineData("2026-08-04 12:00:00 Imp: You hit Training Dummy with your Fire Ball for 12 points of Fire damage.", ParserEventKind.TimestampedLine, false)]
    [InlineData("2026-08-04 12:00:00 utterly unknown future text", ParserEventKind.TimestampedLine, false)]
    [InlineData("2026-99-04 12:00:00 You hit Training Dummy for 12 points of Fire damage.", ParserEventKind.Malformed, false)]
    public void Legacy_rejections_and_combat_shaped_diagnostic_are_frozen(
        string line, ParserEventKind expectedKind, bool expectedCombatShapedUnparsed)
    {
        var input = CombatEventParserTestSupport.Classify(line);
        Assert.Equal(expectedKind, input.EventKind);
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out var rejected));
        Assert.Null(rejected);
        Assert.Equal(expectedCombatShapedUnparsed, CombatEventParser.IsCombatShapedUnparsed(input));
    }

    [Fact]
    public void Current_precedence_prefers_specific_self_heal_and_critical_grammars()
    {
        Assert.True(CombatEventParserTestSupport.TryParseLine(
            "2026-08-04 12:00:00 You heal yourself for 15 hit points with Regeneration.", out var heal));
        Assert.Equal(CombatGrammarId.Heal02YouHealYourself, heal.GrammarId);
        Assert.Equal("yourself", heal.TargetName);

        Assert.True(CombatEventParserTestSupport.TryParseLine(
            "2026-08-04 12:00:01 Crey Thorn Mook critically hits you with Bone Shard for 22 points of Lethal damage.", out var critical));
        Assert.Equal(CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower, critical.GrammarId);
        Assert.Equal("Crey Thorn Mook", critical.SourceName);
    }

    [Theory]
    [InlineData("2026-08-04 12:00:00 [NPC] Contact: You hit Training Dummy for 12 points of Fire damage.", ParserEventKind.SystemLine, "system_channel")]
    [InlineData("2026-08-04 12:00:00 [Combat] Example Hero: You hit Training Dummy for 12 points of Fire damage.", ParserEventKind.ChatLine, "channel_chat")]
    public void Channel_speaker_syntax_is_recognized_but_only_raw_text_and_rule_survive(
        string line, ParserEventKind expectedKind, string expectedRule)
    {
        var input = CombatEventParserTestSupport.Classify(line);
        Assert.Equal(expectedKind, input.EventKind);
        Assert.Equal(expectedRule, input.ClassificationRuleId);
        Assert.Equal(line, input.RawLine);
        Assert.Null(input.StructuralEvidence);
        Assert.DoesNotContain(typeof(ParserEvent).GetProperties(), property =>
            property.Name.Contains("Channel", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Speaker", StringComparison.OrdinalIgnoreCase));
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out _));
    }

    [Fact]
    public void Direct_system_combat_line_has_no_explicit_channel_discriminator()
    {
        const string line = "2026-08-04 12:00:00 You hit Training Dummy with your Fire Ball for 12 points of Fire damage.";
        var input = CombatEventParserTestSupport.Classify(line);
        Assert.Equal(ParserEventKind.SystemLine, input.EventKind);
        Assert.Equal("system_prefix", input.ClassificationRuleId);
        Assert.Null(input.StructuralEvidence);
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(input, out _));
    }

    [Theory]
    [InlineData("2026-08-04 12:00:00 Example Villain hits you with their Fire Ball for 12 points of Fire damage.", "Example Villain")]
    [InlineData("2026-08-04 12:00:00 Example Medic heals you with their Aid for 12 hit points.", "Example Medic")]
    public void Attributed_action_remains_structural_evidence_but_is_not_a_current_combat_candidate(
        string line, string expectedActor)
    {
        var input = CombatEventParserTestSupport.Classify(line);
        Assert.Equal(ParserEventKind.PotentialIdentityEvidence, input.EventKind);
        Assert.Equal("system_attributed_action", input.ClassificationRuleId);
        Assert.Equal(ParserStructuralEvidenceKind.SystemAttributedAction, input.StructuralEvidence!.EvidenceKind);
        Assert.Equal(expectedActor, input.StructuralEvidence.CandidateName);
        // The named actor affects the local player; current identity resolution does not adopt it as the local character.
        Assert.False(CharacterIdentityResolver.IsWelcomeEvidence(input));
        Assert.False(CharacterIdentityResolver.IsStrongAttributedEvidence(input));
        Assert.Null(CharacterIdentityResolver.GetStrongCandidateName(input));
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(input, out _));
        Assert.False(CombatEventParser.IsCombatShapedUnparsed(input));
    }
}

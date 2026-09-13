using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

/// <summary>Source-ordered, sanitized real telemetry evidence. No test opens a live game log.</summary>
public sealed class AnalyticsPreSliceMaxChannelFixtureTests
{
    private static readonly Lazy<IReadOnlyList<FixtureLine>> Fixture = new(ReadFixture);

    [Fact]
    public void Fixture_is_copied_is_source_ordered_and_preserves_real_timestamp_envelopes()
    {
        var lines = Fixture.Value;
        Assert.Equal(72, lines.Count);
        Assert.Equal(lines.Count, lines.Select(line => line.SourceLine).Distinct().Count());
        Assert.True(lines.Zip(lines.Skip(1)).All(pair => pair.First.SourceLine < pair.Second.SourceLine));
        Assert.All(lines, line =>
        {
            Assert.StartsWith("2026-09-12 ", line.RawLine, StringComparison.Ordinal);
            var parsed = CombatEventParserTestSupport.Classify(line.RawLine, sequence: line.SourceLine);
            Assert.Equal(line.RawLine, parsed.RawLine);
            Assert.NotNull(parsed.SourceTimestamp);
            Assert.Equal(2026, parsed.SourceTimestamp.Value.Year);
            Assert.Equal(9, parsed.SourceTimestamp.Value.Month);
            Assert.Equal(12, parsed.SourceTimestamp.Value.Day);
        });
        Assert.Contains("[NPC] Dancer:", At(61).RawLine);
        Assert.Contains("[Team] Ally_A:", At(185).RawLine);
        Assert.Contains("Hero_A", At(6).RawLine);
        Assert.DoesNotContain(lines, line => line.RawLine.Contains("C:\\", StringComparison.Ordinal)
            || line.RawLine.Contains("\\accounts\\", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Fire Cages", 320)]
    [InlineData("OVERPOWER", 322)]
    [InlineData("Reactive Interface", 323)]
    [InlineData("Hold", 328)]
    [InlineData("CONTAINMENT", 378)]
    [InlineData("MISSED", 390)]
    [InlineData("streakbreaker", 428)]
    [InlineData("You have defeated", 436)]
    [InlineData("Stun", 440)]
    [InlineData("Particle Burst", 463)]
    [InlineData("Doublehit", 532)]
    [InlineData("Armageddon: Chance for Fire Damage", 633)]
    [InlineData("Transfusion", 790)]
    [InlineData("knock", 1611)]
    [InlineData("Imp:  HIT", 2278)]
    [InlineData("Defiler Essence:  You hit", 4420)]
    [InlineData("Enervating Storm:  You hit", 4429)]
    [InlineData("Ravager Essence:  You hit", 4701)]
    [InlineData("unresistable Unique", 9372)]
    public void Focused_family_examples_retain_verbatim_game_syntax_after_name_substitution(
        string fragment, int sourceLine) =>
        Assert.Contains(fragment, At(sourceLine).RawLine, StringComparison.Ordinal);

    [Fact]
    public void Delivered_received_heals_are_candidates_not_independently_proven_mirrors()
    {
        var panaceaReceived = At(6);
        var panaceaDelivered = At(7);
        Assert.Equal(panaceaReceived.SourceLine + 1, panaceaDelivered.SourceLine);
        Assert.Contains("78.93 health points", panaceaReceived.RawLine);
        Assert.Contains("78.93 health points", panaceaDelivered.RawLine);
        Assert.Contains("Panacea: Chance for +Hit Points/Endurance", panaceaReceived.RawLine);
        Assert.Contains("Panacea: Chance for +Hit Points/Endurance", panaceaDelivered.RawLine);
        Assert.Equal(ParserEventKind.PotentialIdentityEvidence,
            CombatEventParserTestSupport.Classify(panaceaReceived.RawLine).EventKind);
        Assert.Equal(ParserEventKind.SystemLine,
            CombatEventParserTestSupport.Classify(panaceaDelivered.RawLine).EventKind);
        Assert.True(CombatEventParserTestSupport.TryParseLine(panaceaReceived.RawLine, out var received));
        Assert.True(CombatEventParserTestSupport.TryParseLine(panaceaDelivered.RawLine, out var delivered));
        Assert.Equal(CombatGrammarId.Heal05SourceHealsYouWithTheirHealthPoints, received.GrammarId);
        Assert.Equal(CombatGrammarId.Heal04YouHealTargetHealthPoints, delivered.GrammarId);
        Assert.Equal(CombatEventKind.HealingReceived, received.Kind);
        Assert.Equal(CombatEventKind.HealingDealt, delivered.Kind);
        Assert.Equal(received.Amount, delivered.Amount);
        Assert.Equal(received.PowerName, delivered.PowerName);

        var transfusionReceived = At(792);
        var transfusionDelivered = At(793);
        Assert.Equal(transfusionReceived.SourceLine + 1, transfusionDelivered.SourceLine);
        Assert.Contains("421.34 health points", transfusionReceived.RawLine);
        Assert.Contains("421.34 health points", transfusionDelivered.RawLine);
        Assert.Contains("Hero_A", transfusionReceived.RawLine);
        Assert.Contains("Hero_A", transfusionDelivered.RawLine);
        // Actor/power/amount/time/adjacency agree, but neither pair has an event ID or explicit combat channel.
        // The future allowlist must not collapse these on proximity alone.
    }

    [Theory]
    [InlineData(330, 341)] // Same-second player DoT ticks, same actor/target/power/amount/type.
    [InlineData(2348, 2349)] // Adjacent identical Imp DoT hits.
    [InlineData(2298, 2299)] // Adjacent identical non-mirror Hot Feet damage.
    public void Identical_same_family_rows_remain_two_source_events(int firstLine, int secondLine)
    {
        var first = At(firstLine);
        var second = At(secondLine);
        Assert.Equal(first.RawLine, second.RawLine);
        Assert.NotEqual(first.SourceLine, second.SourceLine);
        Assert.Equal(2, Fixture.Value.Count(line => line.SourceLine == firstLine || line.SourceLine == secondLine));
        // They have no cross-family mirror relationship or independent proof of one logical event.
    }

    [Fact]
    public void Adjacent_similar_Hot_Feet_rows_with_distinct_effect_suffix_must_not_be_collapsed()
    {
        Assert.Equal(2297, At(2296).SourceLine + 1);
        Assert.Contains("20.03 points of Fire damage.", At(2296).RawLine);
        Assert.Contains("20.03 points of Fire damage (CONTAINMENT).", At(2297).RawLine);
        Assert.NotEqual(At(2296).RawLine, At(2297).RawLine);
    }

    [Fact]
    public void Real_channel_syntax_is_distinct_from_bare_combat_lines()
    {
        var npc = CombatEventParserTestSupport.Classify(At(61).RawLine);
        var team = CombatEventParserTestSupport.Classify(At(185).RawLine);
        var combat = CombatEventParserTestSupport.Classify(At(633).RawLine);
        Assert.Equal("system_channel", npc.ClassificationRuleId);
        Assert.Equal("channel_chat", team.ClassificationRuleId);
        Assert.Equal("system_prefix", combat.ClassificationRuleId);
        Assert.Equal(ParserEventKind.SystemLine, combat.EventKind);
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(combat, out var eventResult));
        Assert.Equal(CombatGrammarId.Dmg01YouHitWithPower, eventResult.GrammarId);
        Assert.Equal("Armageddon: Chance for Fire Damage", eventResult.PowerName);
        Assert.Null(npc.StructuralEvidence);
        Assert.Null(team.StructuralEvidence);
        Assert.Equal("NPC", npc.SourceChannel);
        Assert.Equal("Team", team.SourceChannel);
        Assert.Null(combat.SourceChannel);
        Assert.DoesNotContain(typeof(ParserEvent).GetProperties(), property =>
            property.Name.Contains("Speaker", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Actual_attributed_action_keeps_identity_structure_and_is_eligible_for_combat_parse()
    {
        var input = CombatEventParserTestSupport.Classify(At(463).RawLine);
        Assert.Equal(ParserEventKind.PotentialIdentityEvidence, input.EventKind);
        Assert.Equal("system_attributed_action", input.ClassificationRuleId);
        Assert.Equal("Ally_B", input.StructuralEvidence!.CandidateName);
        Assert.False(CharacterIdentityResolver.IsStrongAttributedEvidence(input));
        Assert.True(CombatEventParserTestSupport.Parser.TryParse(input, out var parsed));
        Assert.Equal(CombatGrammarId.Dmg06SourceHitsYouWithTheirPower, parsed.GrammarId);
        Assert.Equal("Particle Burst", parsed.PowerName);
        Assert.False(CombatEventParser.IsCombatShapedUnparsed(input));
    }

    private static FixtureLine At(int sourceLine) =>
        Assert.Single(Fixture.Value, line => line.SourceLine == sourceLine);

    private static IReadOnlyList<FixtureLine> ReadFixture() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory,
                "Fixtures", "Combat", "max-channel-2026-09-12.tsv"))
            .Select(line =>
            {
                var separator = line.IndexOf('\t');
                Assert.True(separator > 0);
                return new FixtureLine(int.Parse(line[..separator]), line[(separator + 1)..]);
            })
            .ToArray();

    private sealed record FixtureLine(int SourceLine, string RawLine);
}

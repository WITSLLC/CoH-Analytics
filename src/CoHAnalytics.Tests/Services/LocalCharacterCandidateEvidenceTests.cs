using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class LocalCharacterCandidateEvidenceTests
{
    [Theory]
    [InlineData(
        "2026-07-30 13:43:03 Hell's Vengence heals you with their Panacea: Chance for +Hit Points/Endurance for 78.93 health points.",
        "IncomingHeal",
        "Hell's Vengence",
        "panacea: chance for +hit points/endurance")]
    [InlineData(
        "2026-07-30 13:43:03 You heal Hell's Vengence with Panacea: Chance for +Hit Points/Endurance for 78.93 health points.",
        "OutgoingHeal",
        "Hell's Vengence",
        "panacea: chance for +hit points/endurance")]
    [InlineData(
        "2026-07-30 13:42:33 Hell's Vengence hits you with their Panacea: Chance for +Hit Points/Endurance granting you 7.67 points of endurance.",
        "IncomingHit",
        "Hell's Vengence",
        "panacea: chance for +hit points/endurance")]
    [InlineData(
        "2026-07-30 13:42:33 You hit Hell's Vengence with your Panacea: Chance for +Hit Points/Endurance granting them 7.67 points of endurance.",
        "OutgoingHit",
        "Hell's Vengence",
        "panacea: chance for +hit points/endurance")]
    [InlineData(
        "2026-07-30 13:43:43 HIT Hell's Vengence! Your Hasten power is autohit.",
        "OutgoingAutohit",
        "Hell's Vengence",
        "hasten")]
    [InlineData(
        "2026-07-30 13:43:43 Hell's Vengence HITS you! Hasten power was autohit.",
        "IncomingAutohit",
        "Hell's Vengence",
        "hasten")]
    public void Reciprocal_local_character_halves_parse(
        string line,
        string kindName,
        string displayName,
        string normalizedPower)
    {
        var event_ = Classify(line);
        var kind = Enum.Parse<LocalCandidateHalfKind>(kindName);

        Assert.True(LocalCharacterCandidateEvidence.TryParseHalf(event_, out var half));
        Assert.Equal(kind, half.Kind);
        Assert.Equal(displayName, half.DisplayName);
        Assert.Equal(normalizedPower, half.NormalizedPower);
    }

    [Theory]
    [InlineData("2026-07-30 13:43:10 [Team] Hell's Vengence: testing chat log capture")]
    [InlineData("2026-07-30 13:42:33 Example Hero hits you with their effect.")]
    [InlineData("2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!")]
    [InlineData("2026-09-12 05:42:17 Ravager Essence:  Defiler Essence HITS you! Empowering Burst power was autohit.")]
    public void Chat_welcome_generic_hits_you_and_pet_prefix_are_not_local_candidate_halves(string line)
    {
        var event_ = Classify(line);

        Assert.False(CharacterIdentityResolver.IsStrongAttributedEvidence(event_));
        Assert.False(LocalCharacterCandidateEvidence.TryParseHalf(event_, out _));
    }

    [Theory]
    [InlineData("2026-07-30 13:43:43 HIT Ally! Your Speed Boost power is autohit.", "Ally")]
    [InlineData("2026-07-30 13:43:43 HIT Imp! Your Empowering Burst power is autohit.", "Imp")]
    [InlineData("2026-07-30 13:43:43 HIT Training Dummy! Your Siphon Power power is autohit.", "Training Dummy")]
    public void One_sided_autohit_parses_as_a_half_but_is_not_a_pair(string line, string name)
    {
        var event_ = Classify(line);

        Assert.True(LocalCharacterCandidateEvidence.TryParseHalf(event_, out var half));
        Assert.Equal(LocalCandidateHalfKind.OutgoingAutohit, half.Kind);
        Assert.Equal(name, half.DisplayName);
    }

    [Fact]
    public void Single_autohit_half_is_not_a_completed_pair()
    {
        var outgoing = Classify("2026-07-30 13:43:43 HIT Training Dummy! Your Siphon Power power is autohit.");
        var incoming = Classify("2026-07-30 13:43:44 Enemy HITS you! Particle Burst power was autohit.");

        Assert.True(LocalCharacterCandidateEvidence.TryParseHalf(outgoing, out var left));
        Assert.True(LocalCharacterCandidateEvidence.TryParseHalf(incoming, out var right));
        Assert.False(LocalCharacterCandidateEvidence.HalvesMatch(left, right, TimeSpan.FromSeconds(2)));
    }

    private static ParserEvent Classify(string line) =>
        GameplaySessionTestInfrastructure.Classify(
            line,
            MonitoringContextId.CreateNew(),
            GameplaySessionTestInfrastructure.DefaultSource());
}

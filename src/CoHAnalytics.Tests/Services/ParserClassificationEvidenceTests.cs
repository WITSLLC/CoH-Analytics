using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class ParserClassificationEvidenceTests
{
    private readonly ParserClassifier _classifier = new();

    [Fact]
    public void Bracket_timestamp_welcome_exposes_exact_character_name()
    {
        const string line = "[03:57] Welcome to City of Heroes, Dawn's Vanguard!";
        var result = _classifier.Classify(ParserClassifierTests.Raw(line));

        Assert.Equal(ParserEventKind.PotentialIdentityEvidence, result.EventKind);
        var evidence = Assert.IsType<ParserStructuralEvidence>(result.StructuralEvidence);
        Assert.Equal("Dawn's Vanguard", evidence.CandidateName);
        Assert.Equal(ParserStructuralEvidenceKind.WelcomeAttribution, evidence.EvidenceKind);
        Assert.Equal("Dawn's Vanguard", line.Substring(evidence.EvidenceTextStart, evidence.EvidenceTextLength));
    }

    [Fact]
    public void Welcome_structure_exposes_name_span_without_resolving_identity()
    {
        const string line = "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!";
        var result = _classifier.Classify(ParserClassifierTests.Raw(line));

        Assert.Equal(ParserEventKind.PotentialIdentityEvidence, result.EventKind);
        var evidence = Assert.IsType<ParserStructuralEvidence>(result.StructuralEvidence);
        Assert.Equal("Example Hero", evidence.CandidateName);
        Assert.Equal(ParserStructuralEvidenceKind.WelcomeAttribution, evidence.EvidenceKind);
        Assert.Equal(ParserAttributionStrength.Strong, evidence.AttributionStrength);
        Assert.Equal("Example Hero", line.Substring(evidence.EvidenceTextStart, evidence.EvidenceTextLength));
    }

    [Fact]
    public void Strong_system_attributed_action_exposes_evidence_for_later_account_scoped_matching()
    {
        const string line = "2026-08-04 06:27:10 Example Hero hits you with their effect.";
        var result = _classifier.Classify(ParserClassifierTests.Raw(line));

        var evidence = Assert.IsType<ParserStructuralEvidence>(result.StructuralEvidence);
        Assert.Equal("Example Hero", evidence.CandidateName);
        Assert.Equal(ParserStructuralEvidenceKind.SystemAttributedAction, evidence.EvidenceKind);
    }

    [Fact]
    public void Chat_speaker_and_random_name_mentions_do_not_become_identity_evidence()
    {
        var chat = _classifier.Classify(ParserClassifierTests.Raw(
            "2026-08-04 06:28:14 [General] Example Hero: Another Hero is here"));
        var mention = _classifier.Classify(ParserClassifierTests.Raw(
            "2026-08-04 06:28:14 You hit Another Hero with your effect."));

        Assert.Null(chat.StructuralEvidence);
        Assert.Null(mention.StructuralEvidence);
    }
}

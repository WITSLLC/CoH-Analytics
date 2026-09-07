using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterIdentityResolverTests
{
    [Fact]
    public void Welcome_evidence_is_recognized_without_resolving_identity()
    {
        var line = "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!";
        var event_ = GameplaySessionTestInfrastructure.Classify(
            line,
            MonitoringContextId.CreateNew(),
            GameplaySessionTestInfrastructure.DefaultSource());

        Assert.True(CharacterIdentityResolver.IsWelcomeEvidence(event_));
        Assert.Equal("Example Hero", CharacterIdentityResolver.GetStrongCandidateName(event_));
    }

    [Fact]
    public void System_attributed_action_is_not_local_character_identity_evidence()
    {
        var line = "2026-08-04 06:27:10 Example Hero hits you with their effect.";
        var event_ = GameplaySessionTestInfrastructure.Classify(
            line,
            MonitoringContextId.CreateNew(),
            GameplaySessionTestInfrastructure.DefaultSource());

        Assert.False(CharacterIdentityResolver.IsStrongAttributedEvidence(event_));
        Assert.Null(CharacterIdentityResolver.GetStrongCandidateName(event_));
    }
}

using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class AccountAnonymityServiceTests
{
    [Fact]
    public void Account_name_is_unchanged_when_disabled()
    {
        var service = new AccountAnonymityService(StubInternalFeatureGate.Enabled);

        Assert.False(service.IsEnabled);
        Assert.Equal("altacct1", service.MaskForPresentation("altacct1"));
    }

    [Fact]
    public void Account_name_is_length_preserving_mask_when_enabled()
    {
        var service = new AccountAnonymityService(StubInternalFeatureGate.Enabled);
        service.SetEnabled(true);

        Assert.Equal("████████", service.MaskForPresentation("altacct1"));
        Assert.Equal("Unknown account", service.MaskForPresentation(null));
    }

    [Fact]
    public void Every_application_scoped_instance_defaults_off()
    {
        var firstLaunch = new AccountAnonymityService(StubInternalFeatureGate.Enabled);
        firstLaunch.SetEnabled(true);

        var nextLaunch = new AccountAnonymityService(StubInternalFeatureGate.Enabled);

        Assert.True(firstLaunch.IsEnabled);
        Assert.False(nextLaunch.IsEnabled);
    }

    [Fact]
    public void Masking_does_not_mutate_monitoring_or_identity_values()
    {
        var context = new LiveMonitoringContextIdentityReadModel
        {
            ContextId = MonitoringContextId.CreateNew(),
            ContextState = MonitoringContextState.Ready,
            AccountStableId = "account-stable-id",
            AccountDisplayName = "altacct1",
            CharacterDisplayName = "TestAccount",
            CharacterIdentityConfidence = CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Resolved,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            IdentityStatusLabel = "Confirmed",
            IdentityDetail = "Active character: TestAccount"
        };
        var service = new AccountAnonymityService(StubInternalFeatureGate.Enabled);
        service.SetEnabled(true);

        Assert.Equal("████████", service.MaskForPresentation(context.AccountDisplayName));
        Assert.Equal("account-stable-id", context.AccountStableId);
        Assert.Equal("altacct1", context.AccountDisplayName);
        Assert.Equal("TestAccount", context.CharacterDisplayName);
    }

    [Fact]
    public void SetEnabled_is_denied_when_developer_mode_is_off()
    {
        var service = new AccountAnonymityService(StubInternalFeatureGate.Disabled);
        service.SetEnabled(true);

        Assert.False(service.IsEnabled);
        Assert.Equal("altacct1", service.MaskForPresentation("altacct1"));
    }

    [Fact]
    public void Default_construction_denies_enable_without_explicit_feature_gate()
    {
        var service = new AccountAnonymityService();
        service.SetEnabled(true);

        Assert.False(service.IsEnabled);
    }
}

using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Integration;

[Collection(AppServicesCompositionCollection.Name)]
public sealed class GameplaySessionContributorCompositionTests
{
    [Fact]
    public async Task App_services_registers_eighth_contributor_with_session_capabilities()
    {
        using var services = AppServices.Create();

        var diagnostics = services.Orchestrator.GetDiagnostics();
        Assert.Equal(9, diagnostics.RegisteredDescriptors.Count);
        Assert.Contains(
            diagnostics.RegisteredDescriptors,
            descriptor => descriptor.ProviderId == ApplicationProviders.Session);

        await services.InitializeOrchestratorAsync();

        diagnostics = services.Orchestrator.GetDiagnostics();
        Assert.Equal(
            ApplicationProviders.Session,
            diagnostics.CapabilityProducers[ApplicationCapabilities.SessionCurrent]);
        Assert.Equal(
            ApplicationProviders.Session,
            diagnostics.CapabilityProducers[ApplicationCapabilities.IdentityCurrent]);

        var sessionProvider = services.Orchestrator.Current.Providers
            .Single(summary => summary.ProviderId == ApplicationProviders.Session);
        Assert.Equal(ApplicationContributorLifecycleState.Running, sessionProvider.LifecycleState);

        var order = diagnostics.TopologicalOrder.ToList();
        var parserIndex = order.IndexOf(ApplicationProviders.Parser);
        var sessionIndex = order.IndexOf(ApplicationProviders.Session);
        Assert.True(parserIndex < sessionIndex);

        Assert.True(services.GameplaySessionManager.GetDiagnostics().IsRunning);

        await services.ShutdownOrchestratorAsync();
        Assert.False(services.GameplaySessionManager.GetDiagnostics().IsRunning);
        Assert.False(Assert.IsType<ParserManager>(services.ParserManager).IsRunning);
    }
}

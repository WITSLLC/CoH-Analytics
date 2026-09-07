using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Integration;

[Collection(AppServicesCompositionCollection.Name)]
public sealed class GameplaySessionManagerCompositionTests
{
    [Fact]
    public async Task App_services_registers_gameplay_session_contributor_as_lifecycle_owner()
    {
        using var services = AppServices.Create();

        Assert.NotNull(services.GameplaySessionManager);
        Assert.IsType<GameplaySessionManager>(services.GameplaySessionManager);
        Assert.Equal(9, services.Orchestrator.GetDiagnostics().RegisteredDescriptors.Count);

        await services.InitializeOrchestratorAsync();
        var diagnostics = services.GameplaySessionManager.GetDiagnostics();
        Assert.True(diagnostics.IsRunning);

        await services.ShutdownOrchestratorAsync();
        diagnostics = services.GameplaySessionManager.GetDiagnostics();
        Assert.False(diagnostics.IsRunning);
    }
}

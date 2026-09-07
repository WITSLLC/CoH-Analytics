using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Integration;

[Collection(AppServicesCompositionCollection.Name)]
public sealed class ParserManagerCompositionTests
{
    [Fact]
    public async Task App_services_exposes_parser_classification_and_registers_the_seventh_contributor()
    {
        using var services = AppServices.Create();

        Assert.NotNull(services.ParserManager);
        Assert.NotNull(services.ParserClassifier);
        Assert.IsType<ParserManager>(services.ParserManager);
        Assert.Equal(9, services.Orchestrator.GetDiagnostics().RegisteredDescriptors.Count);
        Assert.Contains(
            services.Orchestrator.GetDiagnostics().RegisteredDescriptors,
            descriptor => descriptor.ProviderId == "parser");

        await services.InitializeOrchestratorAsync();
        await services.InitializeOrchestratorAsync();

        Assert.True(Assert.IsType<ParserManager>(services.ParserManager).IsRunning);
        Assert.Equal(9, services.Orchestrator.Current.Providers.Count);
        Assert.Equal(
            "parser",
            services.Orchestrator.GetDiagnostics().CapabilityProducers["parser.events"]);

        await services.ShutdownOrchestratorAsync();
        await services.ShutdownOrchestratorAsync();

        Assert.False(Assert.IsType<ParserManager>(services.ParserManager).IsRunning);
    }
}

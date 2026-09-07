using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Integration;

[Collection(AppServicesCompositionCollection.Name)]
public sealed class CharacterRepositoryCompositionTests
{
  [Fact]
  public void App_services_exposes_character_repository_without_adding_contributors()
  {
    using var services = AppServices.Create();

    Assert.NotNull(services.CharacterRepository);
    Assert.IsType<CharacterRepository>(services.CharacterRepository);
        Assert.Equal(9, services.Orchestrator.GetDiagnostics().RegisteredDescriptors.Count);
  }
}

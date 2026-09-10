using CoHAnalytics.Homecoming;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Integration;

[Collection(AppServicesCompositionCollection.Name)]
public sealed class PowerReferenceCatalogCompositionTests
{
    [Fact]
    public void AppServices_ExposesInstalledClientPowerReferenceCatalog()
    {
        using var services = AppServices.Create();

        Assert.IsType<HomecomingPowerReferenceCatalog>(services.PowerReferenceCatalog);
    }
}

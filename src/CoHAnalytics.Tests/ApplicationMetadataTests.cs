using System.Reflection;

namespace CoHAnalytics.Tests;

public sealed class ApplicationMetadataTests
{
    [Fact]
    public void Product_name_comes_from_application_assembly_metadata()
    {
        var assembly = typeof(ApplicationMetadata).Assembly;
        var expected = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;

        Assert.NotNull(expected);
        Assert.Equal(expected, ApplicationMetadata.ProductName);
    }

    [Fact]
    public void Version_text_comes_from_application_informational_version()
    {
        var expected = typeof(ApplicationMetadata).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        Assert.Equal("0.1.3-beta", expected);
        Assert.Equal(expected, ApplicationMetadata.Version);
        Assert.Equal("Beta", ApplicationMetadata.ReleaseStatus);
        Assert.Equal("Version 0.1.3 Beta", ApplicationMetadata.VersionLabel);
        Assert.Equal(
            $"{ApplicationMetadata.ProductName} 0.1.3 Beta",
            ApplicationMetadata.ProductVersionLabel);
        var releaseVersion = Assert.IsType<CoHAnalytics.Updates.ReleaseVersion>(
            ApplicationMetadata.CurrentReleaseVersion);
        Assert.Equal("0.1.3-beta", releaseVersion.CanonicalText);
        Assert.Equal(CoHAnalytics.Updates.DeploymentType.SourceBuild, ApplicationMetadata.DeploymentType);
    }
}

using System.Reflection;
using CoHAnalytics.Updates;

namespace CoHAnalytics;

public static class ApplicationMetadata
{
    private static readonly Assembly ApplicationAssembly = typeof(ApplicationMetadata).Assembly;

    public static string ProductName { get; } =
        ApplicationAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
        ?? ApplicationAssembly.GetName().Name
        ?? "CoH Analytics";

    public static string Version { get; } =
        ApplicationAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? ApplicationAssembly.GetName().Version?.ToString()
        ?? "Unknown";

    public static ReleaseVersion? CurrentReleaseVersion { get; } =
        ReleaseVersion.TryParse(Version, out var parsedVersion) ? parsedVersion : null;

    public static DeploymentType DeploymentType { get; } = ResolveDeploymentType();

    public static string ReleaseStatus => CurrentReleaseVersion?.IsBeta == true ? "Beta" : string.Empty;

    public static string DisplayVersion => CurrentReleaseVersion?.DisplayText ?? Version;

    public static string VersionLabel => $"Version {DisplayVersion}";

    public static string ProductVersionLabel => $"{ProductName} {DisplayVersion}";

    private static DeploymentType ResolveDeploymentType()
    {
        var value = ApplicationAssembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute =>
                string.Equals(attribute.Key, "CoHAnalytics.DeploymentType", StringComparison.Ordinal))
            ?.Value;

        return Enum.TryParse<DeploymentType>(value, ignoreCase: false, out var deploymentType)
            ? deploymentType
            : DeploymentType.Unknown;
    }
}

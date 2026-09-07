using System.Reflection;

namespace CoHAnalytics;

public static class ApplicationMetadata
{
    public const string ReleaseStatus = "Beta";

    private static readonly Assembly ApplicationAssembly = typeof(ApplicationMetadata).Assembly;

    public static string ProductName { get; } =
        ApplicationAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
        ?? ApplicationAssembly.GetName().Name
        ?? "CoH Analytics";

    public static string Version { get; } =
        ApplicationAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? ApplicationAssembly.GetName().Version?.ToString()
        ?? "Unknown";

    public static string VersionLabel => $"Version {Version} {ReleaseStatus}";

    public static string ProductVersionLabel => $"{ProductName} {Version} {ReleaseStatus}";
}

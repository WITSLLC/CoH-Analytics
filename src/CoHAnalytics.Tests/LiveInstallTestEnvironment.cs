using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests;

internal static class LiveInstallTestEnvironment
{
    internal const string EnvironmentVariableName = "COH_ANALYTICS_HOMECOMING_INSTALL_ROOT";

    private const string ClientExecutableRelativePath = @"bin\win64\live\cityofheroes.exe";

    private static readonly Lazy<string> ResolvedInstallRoot = new(ResolveInstallRoot);

    internal static string InstallRoot => ResolvedInstallRoot.Value;

    private static string ResolveInstallRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            throw new InvalidOperationException(
                $"Live-install tests require a Homecoming installation. Set {EnvironmentVariableName} "
                + "to the Homecoming install root, then run the explicit live test profile.");
        }

        HomecomingStaticDataSource source;
        try
        {
            source = HomecomingStaticDataSourceDiscovery.Discover(configuredRoot);
        }
        catch (Exception exception) when (exception is HomecomingStaticDataSourceException
                                          or ArgumentException
                                          or NotSupportedException
                                          or PathTooLongException)
        {
            throw new InvalidOperationException(
                $"The Homecoming installation configured by {EnvironmentVariableName} is invalid. "
                + exception.Message,
                exception);
        }

        var clientExecutablePath = Path.Combine(source.InstallRoot, ClientExecutableRelativePath);
        if (!File.Exists(clientExecutablePath))
        {
            throw new InvalidOperationException(
                $"The Homecoming installation configured by {EnvironmentVariableName} is invalid. "
                + $"Required live client executable was not found: '{clientExecutablePath}'.");
        }

        if (HomecomingTextureArtworkArchiveSet.TryOpen(source.InstallRoot) is null)
        {
            throw new InvalidOperationException(
                $"The Homecoming installation configured by {EnvironmentVariableName} is invalid. "
                + "No readable Homecoming artwork PIGG was found in its assets directories.");
        }

        return source.InstallRoot;
    }
}

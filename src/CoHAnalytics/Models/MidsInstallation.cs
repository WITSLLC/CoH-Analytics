using CoHAnalytics.Services;

namespace CoHAnalytics.Models;

public sealed class MidsInstallation
{
    public required string InstallRoot { get; init; }

    public required string ExecutablePath { get; init; }

    public string? ApplicationVersion { get; init; }

    public required string HomecomingDatabasePath { get; init; }

    public string? HomecomingDatabaseVersion { get; init; }

    public MidsDiscoverySource DiscoverySource { get; init; }
}

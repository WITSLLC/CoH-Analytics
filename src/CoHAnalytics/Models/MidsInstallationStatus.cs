using CoHAnalytics.Services;

namespace CoHAnalytics.Models;

public sealed class MidsInstallationStatus
{
    public bool IsConfigured { get; init; }

    public bool IsValid { get; init; }

    public string? InstallPath { get; init; }

    public string? ExecutablePath { get; init; }

    public string? ApplicationVersion { get; init; }

    public string? HomecomingDatabasePath { get; init; }

    public string? HomecomingDatabaseVersion { get; init; }

    public MidsDiscoverySource? DiscoverySource { get; init; }

    public bool MultipleInstallAmbiguity { get; init; }

    public static MidsInstallationStatus FromInstallation(MidsInstallation? installation, bool multipleInstallAmbiguity = false)
    {
        if (installation is null)
        {
            return new MidsInstallationStatus
            {
                IsConfigured = false,
                IsValid = false,
                MultipleInstallAmbiguity = multipleInstallAmbiguity
            };
        }

        return new MidsInstallationStatus
        {
            IsConfigured = true,
            IsValid = true,
            InstallPath = installation.InstallRoot,
            ExecutablePath = installation.ExecutablePath,
            ApplicationVersion = installation.ApplicationVersion,
            HomecomingDatabasePath = installation.HomecomingDatabasePath,
            HomecomingDatabaseVersion = installation.HomecomingDatabaseVersion,
            DiscoverySource = installation.DiscoverySource,
            MultipleInstallAmbiguity = multipleInstallAmbiguity
        };
    }
}

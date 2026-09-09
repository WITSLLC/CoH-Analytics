namespace CoHAnalytics.Models;

public sealed class AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public string? HomecomingInstallPath { get; set; }

    public string? HomecomingLauncherPath { get; set; }

    public string? MidsInstallPath { get; set; }

    public string? MidsExecutablePath { get; set; }

    /// <summary>
    /// Optional override for app-owned, user-imported character portraits. A null value uses
    /// <c>%LocalAppData%\CoH Analytics\User Icons</c>.
    /// </summary>
    public string? UserCharacterIconsPath { get; set; }

    /// <summary>
    /// Hidden internal feature token. Not shown in Settings or any normal UI.
    /// Absent or null disables developer mode. Interpreted only by <c>IInternalFeatureGate</c>.
    /// </summary>
    public string? AngiesView { get; set; }

    public DateTimeOffset? LastUpdateNotificationUtc { get; set; }

    public string? LastNotifiedReleaseVersion { get; set; }
}

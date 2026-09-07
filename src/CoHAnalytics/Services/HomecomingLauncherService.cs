using System.Diagnostics;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public sealed class HomecomingLauncherService
{
    private readonly HomecomingInstallationService _installationService;
    private readonly SettingsService _settingsService;
    private int _launchInProgress;

    public HomecomingLauncherService(
        SettingsService settingsService,
        HomecomingInstallationService installationService)
    {
        _settingsService = settingsService;
        _installationService = installationService;
    }

    public async Task<string?> LaunchAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _launchInProgress, 1, 0) != 0)
        {
            return null;
        }

        try
        {
            return await Task.Run(() => LaunchCore(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _launchInProgress, 0);
        }
    }

    private string? LaunchCore()
    {
        var installation = _installationService.CurrentInstallation
                           ?? _installationService.DiscoverAndPersist();

        if (installation is null)
        {
            return "Homecoming installation not found. Configure the installation path in Settings.";
        }

        var settings = _settingsService.Load();
        string launcherPath;

        try
        {
            launcherPath = HomecomingPathRules.ResolveLauncherPath(
                installation.InstallRoot,
                settings.HomecomingLauncherPath);
        }
        catch (FileNotFoundException)
        {
            return "Homecoming launcher not found. Verify the installation path in Settings.";
        }

        if (!File.Exists(launcherPath))
        {
            return "Homecoming launcher not found. Verify the installation path in Settings.";
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = launcherPath,
                WorkingDirectory = installation.InstallRoot,
                UseShellExecute = true
            };

            Process.Start(startInfo);
            return null;
        }
        catch (Exception ex)
        {
            return $"Unable to launch Homecoming: {ex.Message}";
        }
    }
}

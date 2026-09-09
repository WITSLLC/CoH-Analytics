using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class SettingsServiceUpdateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void Atomic_update_preserves_unrelated_settings()
    {
        var service = new SettingsService(_directory);
        service.Save(new AppSettings
        {
            HomecomingInstallPath = "C:\\Games\\Homecoming",
            MidsExecutablePath = "C:\\Tools\\Mids.exe",
            AngiesView = "kept"
        });
        var notificationTime = DateTimeOffset.Parse("2026-09-09T12:00:00Z");

        service.Update(settings =>
        {
            settings.LastUpdateNotificationUtc = notificationTime;
            settings.LastNotifiedReleaseVersion = "0.1.3-beta";
        });

        var settings = service.Load();
        Assert.Equal("C:\\Games\\Homecoming", settings.HomecomingInstallPath);
        Assert.Equal("C:\\Tools\\Mids.exe", settings.MidsExecutablePath);
        Assert.Equal("kept", settings.AngiesView);
        Assert.Equal(notificationTime, settings.LastUpdateNotificationUtc);
        Assert.Equal("0.1.3-beta", settings.LastNotifiedReleaseVersion);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

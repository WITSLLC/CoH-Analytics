using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class HomecomingInstallationServiceConfigureTests : IDisposable
{
    private readonly string _settingsDirectory;
    private readonly SettingsService _settingsService;
    private readonly HomecomingInstallationService _service;
    private readonly string _validInstallRoot;
    private readonly string _alternateInstallRoot;

    public HomecomingInstallationServiceConfigureTests()
    {
        _settingsDirectory = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-settings-test-" + Guid.NewGuid().ToString("n"));
        _settingsService = new SettingsService(_settingsDirectory);
        _service = new HomecomingInstallationService(_settingsService);
        _validInstallRoot = HomecomingRuntimeTestSupport.CreateInstallRoot();
        _alternateInstallRoot = HomecomingRuntimeTestSupport.CreateInstallRoot();
    }

    public void Dispose()
    {
        Directory.Delete(_validInstallRoot, recursive: true);
        Directory.Delete(_alternateInstallRoot, recursive: true);
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryConfigureInstallRoot_persists_valid_selection_to_authoritative_settings()
    {
        Assert.True(_service.TryConfigureInstallRoot(_validInstallRoot, out var failureReason));
        Assert.Null(failureReason);
        Assert.Equal(_validInstallRoot, _service.CurrentInstallation?.InstallRoot);

        var settings = _settingsService.Load();
        Assert.Equal(_validInstallRoot, settings.HomecomingInstallPath);
    }

    [Fact]
    public void TryConfigureInstallRoot_rejects_invalid_directory_without_replacing_current_installation()
    {
        Assert.True(_service.TryConfigureInstallRoot(_validInstallRoot, out _));

        Assert.False(_service.TryConfigureInstallRoot(Path.GetTempPath(), out var failureReason));
        Assert.False(string.IsNullOrWhiteSpace(failureReason));
        Assert.Equal(_validInstallRoot, _service.CurrentInstallation?.InstallRoot);

        var settings = _settingsService.Load();
        Assert.Equal(_validInstallRoot, settings.HomecomingInstallPath);
    }

    [Fact]
    public void TryConfigureInstallRoot_updates_authoritative_settings_when_replacing_valid_installation()
    {
        Assert.True(_service.TryConfigureInstallRoot(_validInstallRoot, out _));
        Assert.True(_service.TryConfigureInstallRoot(_alternateInstallRoot, out _));

        Assert.Equal(_alternateInstallRoot, _service.CurrentInstallation?.InstallRoot);
        var settings = _settingsService.Load();
        Assert.Equal(_alternateInstallRoot, settings.HomecomingInstallPath);
    }

    [Fact]
    public void DiscoverAndPersist_reads_persisted_authoritative_path_on_restart()
    {
        Assert.True(_service.TryConfigureInstallRoot(_validInstallRoot, out _));

        var restartedService = new HomecomingInstallationService(_settingsService);
        var installation = restartedService.DiscoverAndPersist();

        Assert.NotNull(installation);
        Assert.Equal(_validInstallRoot, installation.InstallRoot);
        Assert.Equal(HomecomingDiscoverySource.Persisted, restartedService.LastDiscovery.FinalSelectedSource);
    }
}

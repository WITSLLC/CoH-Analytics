using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class SettingsViewModelHomecomingInstallTests : IDisposable
{
    private readonly string _settingsDirectory;
    private readonly SettingsService _settingsService;
    private readonly HomecomingInstallationService _installationService;
    private readonly string _validInstallRoot;
    private readonly string _alternateInstallRoot;
    private readonly RecordingFolderInteractionService _folderInteractionService = new();

    public SettingsViewModelHomecomingInstallTests()
    {
        _settingsDirectory = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-settings-vm-test-" + Guid.NewGuid().ToString("n"));
        _settingsService = new SettingsService(_settingsDirectory);
        _installationService = new HomecomingInstallationService(_settingsService);
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
    public void Settings_displays_current_authoritative_homecoming_install_root()
    {
        Assert.True(_installationService.TryConfigureInstallRoot(_validInstallRoot, out _));

        using var viewModel = CreateViewModel();

        Assert.Equal(_validInstallRoot, viewModel.HomecomingInstallPathLabel);
    }

    [Fact]
    public void Settings_shows_auto_detected_status_only_when_discovery_source_is_not_persisted()
    {
        Assert.True(_installationService.TryConfigureInstallRoot(_validInstallRoot, out _));

        using var persistedViewModel = CreateViewModel();
        Assert.False(persistedViewModel.ShowHomecomingInstallStatus);
        Assert.Equal(string.Empty, persistedViewModel.HomecomingInstallStatusLabel);

        var discovery = new HomecomingInstallDiscoveryDiagnostics();
        discovery.RecordAttempt(HomecomingDiscoverySource.OfficialDefault, _validInstallRoot, true, null);
        discovery.RecordSelection(_validInstallRoot, HomecomingDiscoverySource.OfficialDefault, "launcher.exe");
        typeof(HomecomingInstallationService)
            .GetProperty(nameof(HomecomingInstallationService.LastDiscovery))!
            .SetValue(_installationService, discovery);

        using var autoDetectedViewModel = CreateViewModel();
        Assert.True(autoDetectedViewModel.ShowHomecomingInstallStatus);
        Assert.Equal("Detected automatically", autoDetectedViewModel.HomecomingInstallStatusLabel);
    }

    [Fact]
    public void Invalid_configure_attempt_does_not_replace_settings_path()
    {
        Assert.True(_installationService.TryConfigureInstallRoot(_validInstallRoot, out _));

        Assert.False(_installationService.TryConfigureInstallRoot(Path.GetTempPath(), out _));
        Assert.Equal(_validInstallRoot, _settingsService.Load().HomecomingInstallPath);
    }

    [Fact]
    public void Browse_command_persists_valid_selection_and_refreshes_display()
    {
        Assert.True(_installationService.TryConfigureInstallRoot(_validInstallRoot, out _));
        _folderInteractionService.NextSelection = _alternateInstallRoot;

        using var viewModel = CreateViewModel();
        viewModel.BrowseHomecomingInstallCommand.Execute(null);

        Assert.Equal(_alternateInstallRoot, viewModel.HomecomingInstallPathLabel);
        Assert.Equal(_alternateInstallRoot, _settingsService.Load().HomecomingInstallPath);
        Assert.Contains("Restart", viewModel.HomecomingInstallFeedback, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Browse_command_rejects_invalid_selection_and_preserves_prior_path()
    {
        Assert.True(_installationService.TryConfigureInstallRoot(_validInstallRoot, out _));
        _folderInteractionService.NextSelection = Path.GetTempPath();

        using var viewModel = CreateViewModel();
        viewModel.BrowseHomecomingInstallCommand.Execute(null);

        Assert.Equal(_validInstallRoot, viewModel.HomecomingInstallPathLabel);
        Assert.Equal(_validInstallRoot, _settingsService.Load().HomecomingInstallPath);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.HomecomingInstallFeedback));
    }

    private SettingsViewModel CreateViewModel() =>
        new(
            new FakeApplicationOrchestrator(),
            new FakeGameRuntimeService(),
            _installationService,
            _settingsService,
            new SessionStore(_settingsDirectory),
            _folderInteractionService);

    private sealed class RecordingFolderInteractionService : IFolderInteractionService
    {
        public string? NextSelection { get; set; }

        public string? SelectFolder(string title, string? initialDirectory = null)
        {
            var selection = NextSelection;
            NextSelection = null;
            return selection;
        }

        public bool TryOpenFolder(string path, out string? failureReason)
        {
            failureReason = null;
            return true;
        }
    }

    private sealed class FakeApplicationOrchestrator : IApplicationOrchestrator
    {
        public ApplicationStateSnapshot Current { get; } =
            ApplicationStateSnapshot.Empty(DateTimeOffset.UtcNow);

        public event EventHandler<ApplicationStateChangedEventArgs>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task RefreshAsync(string? providerId = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

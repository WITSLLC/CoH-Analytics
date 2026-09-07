using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class SettingsViewModelFileLocationTests : IDisposable
{
    private readonly string _applicationRoot = Path.Combine(
        Path.GetTempPath(),
        "coh-analytics-file-locations-test-" + Guid.NewGuid().ToString("n"));
    private readonly SettingsService _settingsService;
    private readonly HomecomingInstallationService _homecomingInstallationService;
    private readonly SessionStore _sessionStore;
    private readonly RecordingFolderInteractionService _folderInteractionService = new();

    public SettingsViewModelFileLocationTests()
    {
        _settingsService = new SettingsService(_applicationRoot);
        _homecomingInstallationService = new HomecomingInstallationService(_settingsService);
        _sessionStore = new SessionStore(_applicationRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_applicationRoot))
        {
            Directory.Delete(_applicationRoot, recursive: true);
        }
    }

    [Fact]
    public void Default_user_icons_path_uses_application_data_convention()
    {
        using var viewModel = CreateViewModel();

        Assert.Equal(
            ApplicationDataPaths.GetUserCharacterIconsDirectory(_applicationRoot),
            viewModel.UserCharacterIconsPathLabel);
        Assert.Null(_settingsService.Load().UserCharacterIconsPath);
    }

    [Fact]
    public void Change_user_icons_path_creates_and_persists_custom_location()
    {
        var customPath = Path.Combine(_applicationRoot, "Custom Portraits");
        _folderInteractionService.NextSelection = customPath;

        using (var viewModel = CreateViewModel())
        {
            viewModel.ChangeUserCharacterIconsFolderCommand.Execute(null);

            Assert.Equal(customPath, viewModel.UserCharacterIconsPathLabel);
            Assert.True(Directory.Exists(customPath));
            Assert.Contains("not moved", viewModel.UserCharacterIconsFeedback, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(customPath, _settingsService.Load().UserCharacterIconsPath);
        using var reopenedViewModel = CreateViewModel();
        Assert.Equal(customPath, reopenedViewModel.UserCharacterIconsPathLabel);
    }

    [Fact]
    public void Reset_user_icons_path_restores_default_without_moving_custom_folder()
    {
        var customPath = Path.Combine(_applicationRoot, "Custom Portraits");
        Directory.CreateDirectory(customPath);
        var settings = _settingsService.Load();
        settings.UserCharacterIconsPath = customPath;
        _settingsService.Save(settings);

        using var viewModel = CreateViewModel();
        viewModel.ResetUserCharacterIconsFolderCommand.Execute(null);

        var defaultPath = ApplicationDataPaths.GetUserCharacterIconsDirectory(_applicationRoot);
        Assert.Equal(defaultPath, viewModel.UserCharacterIconsPathLabel);
        Assert.True(Directory.Exists(defaultPath));
        Assert.True(Directory.Exists(customPath));
        Assert.Null(_settingsService.Load().UserCharacterIconsPath);
    }

    [Fact]
    public void Open_user_icons_folder_creates_the_active_location_before_opening()
    {
        using var viewModel = CreateViewModel();

        viewModel.OpenUserCharacterIconsFolderCommand.Execute(null);

        Assert.True(Directory.Exists(viewModel.UserCharacterIconsPathLabel));
        Assert.Equal(viewModel.UserCharacterIconsPathLabel, _folderInteractionService.OpenedPath);
    }

    [Fact]
    public void Saved_sessions_path_comes_from_authoritative_session_store_and_is_open_only()
    {
        using var viewModel = CreateViewModel();

        Assert.Equal(
            ApplicationDataPaths.GetSavedSessionsDirectory(_applicationRoot),
            viewModel.SavedSessionsPathLabel);

        viewModel.OpenSavedSessionsFolderCommand.Execute(null);

        Assert.True(Directory.Exists(viewModel.SavedSessionsPathLabel));
        Assert.Equal(viewModel.SavedSessionsPathLabel, _folderInteractionService.OpenedPath);
    }

    [Fact]
    public void Invalid_user_icons_selection_preserves_previous_setting()
    {
        var customPath = Path.Combine(_applicationRoot, "Custom Portraits");
        Directory.CreateDirectory(customPath);
        var settings = _settingsService.Load();
        settings.UserCharacterIconsPath = customPath;
        _settingsService.Save(settings);
        _folderInteractionService.NextSelection = "relative-folder";

        using var viewModel = CreateViewModel();
        viewModel.ChangeUserCharacterIconsFolderCommand.Execute(null);

        Assert.Equal(customPath, viewModel.UserCharacterIconsPathLabel);
        Assert.Equal(customPath, _settingsService.Load().UserCharacterIconsPath);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.UserCharacterIconsFeedback));
    }

    private SettingsViewModel CreateViewModel() =>
        new(
            new FakeApplicationOrchestrator(),
            new FakeGameRuntimeService(),
            _homecomingInstallationService,
            _settingsService,
            _sessionStore,
            _folderInteractionService);

    private sealed class RecordingFolderInteractionService : IFolderInteractionService
    {
        public string? NextSelection { get; set; }

        public string? OpenedPath { get; private set; }

        public string? SelectFolder(string title, string? initialDirectory = null)
        {
            var selection = NextSelection;
            NextSelection = null;
            return selection;
        }

        public bool TryOpenFolder(string path, out string? failureReason)
        {
            OpenedPath = path;
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

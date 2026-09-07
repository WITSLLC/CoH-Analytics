using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.Workspaces;

public sealed partial class SettingsViewModel : WorkspaceEnvironmentStatusViewModelBase, IDisposable
{
    private readonly HomecomingInstallationService _homecomingInstallationService;
    private readonly SettingsService _settingsService;
    private readonly IFolderInteractionService _folderInteractionService;
    private readonly string _defaultUserCharacterIconsDirectory;
    private bool _disposed;

    public SettingsViewModel(
        IApplicationOrchestrator orchestrator,
        IGameRuntimeService gameRuntimeService,
        HomecomingInstallationService homecomingInstallationService,
        SettingsService settingsService,
        ISessionStore sessionStore,
        IFolderInteractionService folderInteractionService)
        : base(orchestrator, gameRuntimeService)
    {
        _homecomingInstallationService = homecomingInstallationService;
        _settingsService = settingsService;
        _folderInteractionService = folderInteractionService;

        var applicationRoot = Path.GetDirectoryName(settingsService.SettingsPath)
                              ?? ApplicationDataPaths.GetApplicationRoot();
        _defaultUserCharacterIconsDirectory =
            ApplicationDataPaths.GetUserCharacterIconsDirectory(applicationRoot);
        SavedSessionsPathLabel = Path.Combine(sessionStore.SessionRootDirectory, "Saved");

        WireEnvironmentStatus();
        RefreshHomecomingInstallPresentation();
        RefreshUserCharacterIconsPresentation();
    }

    public override string Title => "Settings";

    public string Description => "Game installation and application configuration.";

    public string SavedSessionsPathLabel { get; }

    public string DefaultUserCharacterIconsDirectory => _defaultUserCharacterIconsDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHomecomingInstallStatus))]
    private string _homecomingInstallPathLabel = string.Empty;

    [ObservableProperty]
    private string _homecomingInstallStatusLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHomecomingInstallFeedback))]
    private string _homecomingInstallFeedback = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedSessionsFeedback))]
    private string _savedSessionsFeedback = string.Empty;

    [ObservableProperty]
    private string _userCharacterIconsPathLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUserCharacterIconsFeedback))]
    private string _userCharacterIconsFeedback = string.Empty;

    public bool ShowHomecomingInstallStatus => !string.IsNullOrWhiteSpace(HomecomingInstallStatusLabel);

    public bool HasHomecomingInstallFeedback => !string.IsNullOrWhiteSpace(HomecomingInstallFeedback);

    public bool HasSavedSessionsFeedback => !string.IsNullOrWhiteSpace(SavedSessionsFeedback);

    public bool HasUserCharacterIconsFeedback => !string.IsNullOrWhiteSpace(UserCharacterIconsFeedback);

    [RelayCommand]
    private void BrowseHomecomingInstall()
    {
        HomecomingInstallFeedback = string.Empty;

        var currentPath = _homecomingInstallationService.CurrentInstallation?.InstallRoot;
        var selectedPath = _folderInteractionService.SelectFolder(
            "Select Homecoming installation folder",
            currentPath);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        if (!_homecomingInstallationService.TryConfigureInstallRoot(selectedPath, out var failureReason))
        {
            HomecomingInstallFeedback =
                failureReason ?? "The selected folder is not a valid Homecoming installation.";
            return;
        }

        RefreshHomecomingInstallPresentation();

        var configuredPath = _homecomingInstallationService.CurrentInstallation?.InstallRoot;
        if (!string.IsNullOrWhiteSpace(configuredPath)
            && !string.Equals(currentPath, configuredPath, StringComparison.OrdinalIgnoreCase))
        {
            HomecomingInstallFeedback =
                "Installation path saved. Restart CoH Analytics for the change to take full effect.";
        }
    }

    [RelayCommand]
    private void OpenSavedSessionsFolder()
    {
        SavedSessionsFeedback = string.Empty;
        if (!TryEnsureDirectory(SavedSessionsPathLabel, out var failureReason)
            || !_folderInteractionService.TryOpenFolder(SavedSessionsPathLabel, out failureReason))
        {
            SavedSessionsFeedback = failureReason ?? "Unable to open the Saved Sessions folder.";
        }
    }

    [RelayCommand]
    private void OpenUserCharacterIconsFolder()
    {
        UserCharacterIconsFeedback = string.Empty;
        if (!TryEnsureDirectory(UserCharacterIconsPathLabel, out var failureReason)
            || !_folderInteractionService.TryOpenFolder(UserCharacterIconsPathLabel, out failureReason))
        {
            UserCharacterIconsFeedback = failureReason ?? "Unable to open the User Icons folder.";
        }
    }

    [RelayCommand]
    private void ChangeUserCharacterIconsFolder()
    {
        UserCharacterIconsFeedback = string.Empty;
        var selectedPath = _folderInteractionService.SelectFolder(
            "Select User Character Icons folder",
            UserCharacterIconsPathLabel);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        if (!TryNormalizeAndCreateDirectory(selectedPath, out var normalizedPath, out var failureReason))
        {
            UserCharacterIconsFeedback = failureReason ?? "The selected folder cannot be used.";
            return;
        }

        try
        {
            var settings = _settingsService.Load();
            settings.UserCharacterIconsPath = string.Equals(
                normalizedPath,
                _defaultUserCharacterIconsDirectory,
                StringComparison.OrdinalIgnoreCase)
                ? null
                : normalizedPath;
            _settingsService.Save(settings);
            UserCharacterIconsPathLabel = normalizedPath;
            UserCharacterIconsFeedback = "Location saved. Existing images were not moved.";
        }
        catch (Exception exception)
        {
            UserCharacterIconsFeedback = $"Unable to save the location: {exception.Message}";
        }
    }

    [RelayCommand]
    private void ResetUserCharacterIconsFolder()
    {
        UserCharacterIconsFeedback = string.Empty;
        if (!TryEnsureDirectory(_defaultUserCharacterIconsDirectory, out var failureReason))
        {
            UserCharacterIconsFeedback = failureReason ?? "Unable to restore the default folder.";
            return;
        }

        try
        {
            var settings = _settingsService.Load();
            settings.UserCharacterIconsPath = null;
            _settingsService.Save(settings);
            UserCharacterIconsPathLabel = _defaultUserCharacterIconsDirectory;
            UserCharacterIconsFeedback = "Default location restored. Existing images were not moved.";
        }
        catch (Exception exception)
        {
            UserCharacterIconsFeedback = $"Unable to restore the default location: {exception.Message}";
        }
    }

    private void RefreshHomecomingInstallPresentation()
    {
        var installation = _homecomingInstallationService.CurrentInstallation;
        HomecomingInstallPathLabel = installation?.InstallRoot ?? "Not configured";

        var discoverySource = _homecomingInstallationService.LastDiscovery.FinalSelectedSource;
        HomecomingInstallStatusLabel = installation is not null
            && discoverySource is HomecomingDiscoverySource source
            && source != HomecomingDiscoverySource.Persisted
            ? "Detected automatically"
            : string.Empty;
    }

    private void RefreshUserCharacterIconsPresentation()
    {
        var configuredPath = _settingsService.Load().UserCharacterIconsPath;
        UserCharacterIconsPathLabel = TryNormalizeDirectory(configuredPath, out var normalizedPath)
            ? normalizedPath
            : _defaultUserCharacterIconsDirectory;
    }

    private static bool TryEnsureDirectory(string path, out string? failureReason)
    {
        try
        {
            Directory.CreateDirectory(path);
            failureReason = null;
            return true;
        }
        catch (Exception exception)
        {
            failureReason = $"Unable to create the folder: {exception.Message}";
            return false;
        }
    }

    private static bool TryNormalizeAndCreateDirectory(
        string? path,
        out string normalizedPath,
        out string? failureReason)
    {
        normalizedPath = string.Empty;
        if (!TryNormalizeDirectory(path, out normalizedPath))
        {
            failureReason = "Select a fully qualified folder path.";
            return false;
        }

        return TryEnsureDirectory(normalizedPath, out failureReason);
    }

    private static bool TryNormalizeDirectory(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
        {
            return false;
        }

        try
        {
            normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnwireEnvironmentStatus();
    }
}

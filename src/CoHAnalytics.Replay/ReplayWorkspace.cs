using System.Text.Json;

namespace CoHAnalytics.Replay;

public sealed class ReplayWorkspace : IAsyncDisposable
{
    public const string DefaultAccountFolderName = "replay-account-01";

    private static readonly JsonSerializerOptions SettingsSerializerOptions = new() { WriteIndented = true };

    private readonly bool _keepWorkspace;
    private bool _disposed;

    private ReplayWorkspace(string rootPath, string accountFolderName, bool keepWorkspace)
    {
        RootPath = rootPath;
        AccountFolderName = accountFolderName;
        HomecomingRoot = Path.Combine(rootPath, "homecoming");
        AccountRoot = Path.Combine(HomecomingRoot, "accounts", accountFolderName);
        LogsDirectory = Path.Combine(AccountRoot, "Logs");
        AccountStableId = CoHAnalytics.Services.HomecomingAccountDiscoveryService.CreateStableId(AccountRoot);
        _keepWorkspace = keepWorkspace;
    }

    public string RootPath { get; }

    public string AccountFolderName { get; }

    public string AccountStableId { get; }

    public string HomecomingRoot { get; }

    public string AccountRoot { get; }

    public string LogsDirectory { get; }

    public bool CleanupRequested { get; private set; }

    public bool CleanupCompleted { get; private set; }

    public static async Task<ReplayWorkspace> CreateAsync(
        bool keepWorkspace,
        string? accountFolderName = null,
        CancellationToken cancellationToken = default)
    {
        var folderName = string.IsNullOrWhiteSpace(accountFolderName)
            ? DefaultAccountFolderName
            : accountFolderName;
        var runId = Guid.NewGuid().ToString("n");
        var rootPath = Path.Combine(Path.GetTempPath(), "coh-analytics-replay", runId);
        Directory.CreateDirectory(rootPath);

        var workspace = new ReplayWorkspace(rootPath, folderName, keepWorkspace);
        await workspace.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return workspace;
    }

    public CoHAnalytics.Models.HomecomingAccount ToHomecomingAccount() =>
        new()
        {
            StableId = AccountStableId,
            DisplayName = AccountFolderName,
            FolderName = AccountFolderName,
            FolderPath = AccountRoot,
            HasLogsFolder = true,
            Status = CoHAnalytics.Models.HomecomingAccountStatus.Ready
        };

    public string ResolveDestinationLogPath(DateOnly destinationDate) =>
        Path.Combine(LogsDirectory, $"chatlog {destinationDate:yyyy-MM-dd}.txt");

    public Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = cancellationToken;
        CleanupRequested = true;

        if (_keepWorkspace)
        {
            // Retention was requested: the directory is intentionally not deleted.
            // CleanupCompleted remains false to signal that no deletion occurred.
            return Task.CompletedTask;
        }

        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }

        CleanupCompleted = true;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (!CleanupCompleted && !_keepWorkspace)
        {
            await CleanupAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _disposed = true;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(LogsDirectory);

        var settings = new Dictionary<string, object>
        {
            ["ui.chat.timestamps"] = true,
            ["ui.scale"] = 1.0
        };

        var settingsPath = Path.Combine(AccountRoot, "settings.json");
        Directory.CreateDirectory(AccountRoot);
        await using var settingsStream = new FileStream(
            settingsPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(settingsStream, settings, SettingsSerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }
}

using System.Diagnostics;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.Accounts;

public partial class AccountDetailsViewModel : ObservableObject
{
    private readonly HomecomingAccount _account;

    public AccountDetailsViewModel(
        HomecomingAccount account,
        AccountAnonymityService? accountAnonymityService = null)
    {
        _account = account;
        AccountName = (accountAnonymityService ?? new AccountAnonymityService())
            .MaskForPresentation(account.DisplayName);
    }

    public string AccountName { get; }

    public string StatusLabel => _account.Status switch
    {
        HomecomingAccountStatus.Ready => "READY",
        _ => "NO LOGS"
    };

    public string AccountAvailability => "Account discovered";

    public string FolderPath => _account.FolderPath;

    public string LogsFolderLabel => _account.HasLogsFolder ? "Present" : "Not Found";

    public string HistoricalLogCount => _account.LogFileCount.ToString();

    public string OldestLogTimestamp => FormatTimestamp(_account.OldestLogTimestamp);

    public string NewestLogTimestamp => FormatTimestamp(_account.NewestLogTimestamp);

    public string BuildsFolderLabel => _account.HasBuildsFolder ? "Present" : "Not Found";

    public string MapsFolderLabel => _account.HasMapsFolder ? "Present" : "Not Found";

    public string SettingsFileLabel => _account.HasSettingsFile ? "Present" : "Not Found";

    public string ChatLoggingLabel => "Unknown";

    public string LiveActivityLabel => "Not monitored";

    public string CurrentCharacterLabel => "Unknown";

    public string CurrentSessionLabel => "None";

    public string KnownCharactersLabel => "0";

    public bool CanOpenLogsFolder => _account.HasLogsFolder;

    [RelayCommand]
    private void OpenAccountFolder() => TryOpenFolder(_account.FolderPath);

    [RelayCommand(CanExecute = nameof(CanOpenLogsFolder))]
    private void OpenLogsFolder() => TryOpenFolder(Path.Combine(_account.FolderPath, "Logs"));

    private static string FormatTimestamp(DateTimeOffset? timestamp) =>
        timestamp?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Not available";

    private static void TryOpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // Non-fatal: folder open is best-effort only.
        }
    }
}

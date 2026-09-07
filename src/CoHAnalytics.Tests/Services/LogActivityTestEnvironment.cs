using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Real temporary account folders plus fake account models. Real files are used deliberately so
/// file metadata behavior (length, timestamps, deletion) is exercised rather than simulated.
/// </summary>
internal sealed class LogActivityTestEnvironment : IDisposable
{
    private readonly List<HomecomingAccount> _accounts = [];

    public LogActivityTestEnvironment()
    {
        Root = Path.Combine(Path.GetTempPath(), "coh-analytics-log-activity", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public ManualTimeProvider Time { get; } = new();

    /// <summary>Swappable so a test can hold a scan pass open or observe provider calls.</summary>
    public Func<IReadOnlyList<HomecomingAccount>> AccountsProvider { get; set; } = () => [];

    public IReadOnlyList<HomecomingAccount> Accounts => _accounts;

    public DateOnly Today => DateOnly.FromDateTime(Time.GetLocalNow().DateTime);

    public HomecomingAccount AddAccount(string displayName, bool createLogsFolder = true)
    {
        var folderPath = Path.Combine(Root, "accounts", displayName);
        Directory.CreateDirectory(folderPath);

        if (createLogsFolder)
        {
            Directory.CreateDirectory(Path.Combine(folderPath, "Logs"));
        }

        var account = new HomecomingAccount
        {
            StableId = HomecomingAccountDiscoveryService.CreateStableId(folderPath),
            DisplayName = displayName,
            FolderName = displayName,
            FolderPath = folderPath,
            HasLogsFolder = createLogsFolder,
            Status = createLogsFolder ? HomecomingAccountStatus.Ready : HomecomingAccountStatus.NoLogs
        };

        _accounts.Add(account);
        AccountsProvider = () => Accounts;
        return account;
    }

    public string LogsFolder(HomecomingAccount account) => Path.Combine(account.FolderPath, "Logs");

    public string LogPath(HomecomingAccount account, DateOnly date) =>
        Path.Combine(LogsFolder(account), $"chatlog {date:yyyy-MM-dd}.txt");

    public string WriteLog(HomecomingAccount account, DateOnly date, string content = "")
    {
        var path = LogPath(account, date);
        Directory.CreateDirectory(LogsFolder(account));
        File.WriteAllText(path, content);
        return path;
    }

    public void WriteUnrelatedFile(HomecomingAccount account, string fileName, string content = "x")
    {
        Directory.CreateDirectory(LogsFolder(account));
        File.WriteAllText(Path.Combine(LogsFolder(account), fileName), content);
    }

    public void Append(HomecomingAccount account, DateOnly date, string content = "more text") =>
        File.AppendAllText(LogPath(account, date), content);

    public void Truncate(HomecomingAccount account, DateOnly date, string content = "") =>
        File.WriteAllText(LogPath(account, date), content);

    public void Delete(HomecomingAccount account, DateOnly date) =>
        File.Delete(LogPath(account, date));

    public LogActivityService CreateService(
        TimeSpan? inactivityThreshold = null,
        bool enablePolling = false)
    {
        var options = new LogActivityServiceOptions
        {
            TimeProvider = Time,
            EnablePolling = enablePolling,
            InactivityThreshold = inactivityThreshold ?? TimeSpan.FromSeconds(30),
            PollInterval = TimeSpan.FromMilliseconds(50)
        };

        return new LogActivityService(() => AccountsProvider(), options);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // A temporary directory that cannot be removed must not fail a test.
        }
    }
}

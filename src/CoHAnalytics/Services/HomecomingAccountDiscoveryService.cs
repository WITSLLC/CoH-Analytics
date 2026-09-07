using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public sealed class HomecomingAccountDiscoveryService
{
    private static readonly HashSet<string> IgnoredSharedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Maps"
    };

    private static readonly Regex ChatLogFileNamePattern =
        new(@"^chatlog \d{4}-\d{2}-\d{2}\.txt$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly HomecomingInstallationService _installationService;

    public HomecomingAccountDiscoveryService(HomecomingInstallationService installationService)
    {
        _installationService = installationService;
    }

    public IReadOnlyList<HomecomingAccount> Accounts { get; private set; } = [];

    public HomecomingAccountDiscoveryDiagnostics LastDiscovery { get; private set; } = new();

    public IReadOnlyList<HomecomingAccount> Discover()
    {
        var diagnostics = new HomecomingAccountDiscoveryDiagnostics();
        var installation = _installationService.CurrentInstallation;

        if (installation is null)
        {
            diagnostics.RecordAttempt(null, isValid: false, "Homecoming installation is not configured.");
            Accounts = [];
            LastDiscovery = diagnostics;
            return Accounts;
        }

        var accountsRoot = Path.Combine(installation.InstallRoot, "accounts");
        if (!Directory.Exists(accountsRoot))
        {
            diagnostics.RecordAttempt(accountsRoot, isValid: false, "Accounts directory was not found.");
            Accounts = [];
            LastDiscovery = diagnostics;
            return Accounts;
        }

        var discoveredAccounts = new List<HomecomingAccount>();

        IEnumerable<string> candidateDirectories;
        try
        {
            candidateDirectories = Directory
                .EnumerateDirectories(accountsRoot)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            diagnostics.RecordAttempt(accountsRoot, isValid: false, $"Unable to enumerate accounts directory: {ex.Message}");
            Accounts = [];
            LastDiscovery = diagnostics;
            return Accounts;
        }

        foreach (var candidateDirectory in candidateDirectories)
        {
            var folderName = Path.GetFileName(candidateDirectory);

            if (IgnoredSharedFolderNames.Contains(folderName))
            {
                diagnostics.RecordAttempt(candidateDirectory, isValid: false, "Ignored shared folder.");
                continue;
            }

            if (!TryValidateAccountFolder(candidateDirectory, out var failureReason))
            {
                diagnostics.RecordAttempt(candidateDirectory, isValid: false, failureReason);
                continue;
            }

            var logMetadata = GetLogMetadata(candidateDirectory);
            var account = new HomecomingAccount
            {
                StableId = CreateStableId(candidateDirectory),
                DisplayName = folderName,
                FolderName = folderName,
                FolderPath = HomecomingPathRules.NormalizeDirectory(candidateDirectory),
                HasLogsFolder = logMetadata.HasLogsFolder,
                HasHistoricalLogs = logMetadata.LogFileCount > 0,
                LogFileCount = logMetadata.LogFileCount,
                NewestLogTimestamp = logMetadata.NewestLogTimestamp,
                OldestLogTimestamp = logMetadata.OldestLogTimestamp,
                HasBuildsFolder = Directory.Exists(Path.Combine(candidateDirectory, "Builds")),
                HasMapsFolder = Directory.Exists(Path.Combine(candidateDirectory, "maps")),
                HasSettingsFile = File.Exists(Path.Combine(candidateDirectory, "settings.json")),
                Status = logMetadata.HasLogsFolder
                    ? HomecomingAccountStatus.Ready
                    : HomecomingAccountStatus.NoLogs
            };

            discoveredAccounts.Add(account);
            diagnostics.RecordAttempt(
                candidateDirectory,
                isValid: true,
                failureReason: null,
                logFileCount: logMetadata.LogFileCount,
                newestLogTimestamp: logMetadata.NewestLogTimestamp);
            diagnostics.RecordAcceptedAccount(folderName);
        }

        Accounts = discoveredAccounts
            .OrderBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        LastDiscovery = diagnostics;
        return Accounts;
    }

    internal static string CreateStableId(string folderPath)
    {
        var normalized = HomecomingPathRules.NormalizeDirectory(folderPath);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool TryValidateAccountFolder(string candidateDirectory, out string? failureReason)
    {
        failureReason = null;

        if (!Directory.Exists(candidateDirectory))
        {
            failureReason = "Folder does not exist.";
            return false;
        }

        var hasSettings = File.Exists(Path.Combine(candidateDirectory, "settings.json"));
        var hasBuilds = Directory.Exists(Path.Combine(candidateDirectory, "Builds"));
        var hasLogs = Directory.Exists(Path.Combine(candidateDirectory, "Logs"));
        var hasMaps = Directory.Exists(Path.Combine(candidateDirectory, "maps"));

        if (hasSettings || hasBuilds || hasLogs || hasMaps)
        {
            return true;
        }

        failureReason = "Missing settings.json, Builds, Logs, and maps.";
        return false;
    }

    private static LogMetadata GetLogMetadata(string accountDirectory)
    {
        var logsDirectory = Path.Combine(accountDirectory, "Logs");
        if (!Directory.Exists(logsDirectory))
        {
            return new LogMetadata(false, 0, null, null);
        }

        var logFiles = new List<(DateTime LastWriteTimeUtc, string FullPath)>();

        try
        {
            foreach (var logFile in Directory.EnumerateFiles(logsDirectory, "*.txt", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(logFile);
                if (!ChatLogFileNamePattern.IsMatch(fileName))
                {
                    continue;
                }

                var fileInfo = new FileInfo(logFile);
                logFiles.Add((fileInfo.LastWriteTimeUtc, logFile));
            }
        }
        catch
        {
            return new LogMetadata(true, 0, null, null);
        }

        if (logFiles.Count == 0)
        {
            return new LogMetadata(true, 0, null, null);
        }

        var newest = logFiles
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .First();
        var oldest = logFiles
            .OrderBy(file => file.LastWriteTimeUtc)
            .First();

        return new LogMetadata(
            true,
            logFiles.Count,
            new DateTimeOffset(newest.LastWriteTimeUtc, TimeSpan.Zero).ToLocalTime(),
            new DateTimeOffset(oldest.LastWriteTimeUtc, TimeSpan.Zero).ToLocalTime());
    }

    private readonly record struct LogMetadata(
        bool HasLogsFolder,
        int LogFileCount,
        DateTimeOffset? NewestLogTimestamp,
        DateTimeOffset? OldestLogTimestamp);
}

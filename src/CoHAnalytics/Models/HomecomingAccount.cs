namespace CoHAnalytics.Models;

public sealed class HomecomingAccount
{
    public required string StableId { get; init; }

    public required string DisplayName { get; init; }

    public required string FolderName { get; init; }

    public required string FolderPath { get; init; }

    public bool HasLogsFolder { get; init; }

    public bool HasHistoricalLogs { get; init; }

    public int LogFileCount { get; init; }

    public DateTimeOffset? NewestLogTimestamp { get; init; }

    public DateTimeOffset? OldestLogTimestamp { get; init; }

    public bool HasBuildsFolder { get; init; }

    public bool HasMapsFolder { get; init; }

    public bool HasSettingsFile { get; init; }

    public HomecomingAccountStatus Status { get; init; }
}

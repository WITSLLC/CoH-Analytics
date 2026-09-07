namespace CoHAnalytics.Services;

public sealed class HomecomingAccountDiscoveryDiagnostics
{
    public DateTimeOffset DiscoveryStartedUtc { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<HomecomingAccountDiscoveryAttempt> Attempts => _attempts;

    public IReadOnlyList<string> AcceptedAccountFolderNames => _acceptedAccountFolderNames;

    private readonly List<HomecomingAccountDiscoveryAttempt> _attempts = [];

    private readonly List<string> _acceptedAccountFolderNames = [];

    public void RecordAttempt(
        string? candidateFolder,
        bool isValid,
        string? failureReason,
        int logFileCount = 0,
        DateTimeOffset? newestLogTimestamp = null)
    {
        _attempts.Add(new HomecomingAccountDiscoveryAttempt(
            candidateFolder,
            isValid,
            failureReason,
            logFileCount,
            newestLogTimestamp));
    }

    public void RecordAcceptedAccount(string folderName)
    {
        if (!_acceptedAccountFolderNames.Contains(folderName, StringComparer.OrdinalIgnoreCase))
        {
            _acceptedAccountFolderNames.Add(folderName);
        }
    }
}

public readonly record struct HomecomingAccountDiscoveryAttempt(
    string? CandidateFolder,
    bool IsValid,
    string? FailureReason,
    int LogFileCount,
    DateTimeOffset? NewestLogTimestamp);

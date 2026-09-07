namespace CoHAnalytics.Services;

public sealed class MidsInstallDiscoveryDiagnostics
{
    public DateTimeOffset DiscoveryStartedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string? FinalSelectedPath { get; private set; }

    public MidsDiscoverySource? FinalSelectedSource { get; private set; }

    public string? ExecutableResolved { get; private set; }

    public string? ApplicationVersionFound { get; private set; }

    public string? HomecomingDatabaseVersionFound { get; private set; }

    public bool MultipleInstallAmbiguity { get; private set; }

    public IReadOnlyList<string> AmbiguousInstallRoots => _ambiguousInstallRoots;

    public IReadOnlyList<MidsDiscoveryAttempt> Attempts => _attempts;

    private readonly List<MidsDiscoveryAttempt> _attempts = [];

    private readonly List<string> _ambiguousInstallRoots = [];

    public void RecordAttempt(
        MidsDiscoverySource source,
        string? candidatePath,
        bool isValid,
        string? failureReason)
    {
        _attempts.Add(new MidsDiscoveryAttempt(
            source,
            candidatePath,
            isValid,
            failureReason));
    }

    public void RecordAmbiguity(IEnumerable<string> installRoots)
    {
        MultipleInstallAmbiguity = true;
        foreach (var root in installRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            if (!_ambiguousInstallRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                _ambiguousInstallRoots.Add(root);
            }
        }
    }

    public void RecordSelection(
        string installRoot,
        MidsDiscoverySource source,
        string executablePath,
        string? applicationVersion,
        string? homecomingDatabaseVersion)
    {
        FinalSelectedPath = installRoot;
        FinalSelectedSource = source;
        ExecutableResolved = executablePath;
        ApplicationVersionFound = applicationVersion;
        HomecomingDatabaseVersionFound = homecomingDatabaseVersion;
    }
}

public readonly record struct MidsDiscoveryAttempt(
    MidsDiscoverySource Source,
    string? CandidatePath,
    bool IsValid,
    string? FailureReason);

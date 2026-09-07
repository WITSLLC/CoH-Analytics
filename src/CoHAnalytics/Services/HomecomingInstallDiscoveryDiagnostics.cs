namespace CoHAnalytics.Services;

public sealed class HomecomingInstallDiscoveryDiagnostics
{
    public DateTimeOffset DiscoveryStartedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string? FinalSelectedPath { get; private set; }

    public HomecomingDiscoverySource? FinalSelectedSource { get; private set; }

    public string? LauncherResolved { get; private set; }

    public bool MultipleInstallAmbiguity { get; private set; }

    public IReadOnlyList<string> AmbiguousInstallRoots => _ambiguousInstallRoots;

    public IReadOnlyList<HomecomingDiscoveryAttempt> Attempts => _attempts;

    private readonly List<HomecomingDiscoveryAttempt> _attempts = [];

    private readonly List<string> _ambiguousInstallRoots = [];

    public void RecordAttempt(
        HomecomingDiscoverySource source,
        string? candidatePath,
        bool isValid,
        string? failureReason)
    {
        _attempts.Add(new HomecomingDiscoveryAttempt(
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

    public void RecordSelection(string installRoot, HomecomingDiscoverySource source, string launcherPath)
    {
        FinalSelectedPath = installRoot;
        FinalSelectedSource = source;
        LauncherResolved = launcherPath;
    }
}

public readonly record struct HomecomingDiscoveryAttempt(
    HomecomingDiscoverySource Source,
    string? CandidatePath,
    bool IsValid,
    string? FailureReason);

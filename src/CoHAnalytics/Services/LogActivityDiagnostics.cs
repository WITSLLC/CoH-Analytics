using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// In-memory developer-facing report describing how chat-log activity was observed.
/// </summary>
/// <remarks>
/// Diagnostics never contain chat text or any parsed gameplay data, and file paths have the
/// user-profile portion redacted.
/// </remarks>
public sealed record LogActivityDiagnostics
{
    /// <summary>
    /// How sources are observed. This slice uses a periodic metadata scan and no
    /// <see cref="FileSystemWatcher"/>, so the mode is always <c>PollingOnly</c>.
    /// </summary>
    public string ObservationMode => "PollingOnly";

    public required TimeSpan PollInterval { get; init; }

    public required TimeSpan InactivityThreshold { get; init; }

    public required bool IsPollingEnabled { get; init; }

    public required bool IsRunning { get; init; }

    public required int ScanCount { get; init; }

    public DateTimeOffset? LastScanStartedAt { get; init; }

    public TimeSpan? LastScanDuration { get; init; }

    public required long LastSnapshotRevision { get; init; }

    /// <summary>Scans that observed no semantic change and therefore published nothing.</summary>
    public required int SuppressedNoOpScanCount { get; init; }

    public required IReadOnlyList<LogActivityAccountDiagnostics> Accounts { get; init; }

    public required IReadOnlyList<LogActivitySourceDiagnostics> Sources { get; init; }

    public required IReadOnlyList<string> RecentScanFailures { get; init; }

    /// <summary>
    /// Documented limitation: replacement is inferred from an observed disappear-then-reappear
    /// sequence or from a changed creation timestamp without continuous append growth, because no
    /// NTFS file identifier is available without native interop. NTFS file-system tunneling can
    /// restore the original creation timestamp when a file is recreated under the same name within
    /// roughly fifteen seconds, so a rapid delete-and-recreate that is not seen by any scan may be
    /// reported as truncation or continuous growth rather than replacement.
    /// </summary>
    public string ReplacementDetectionLimitation =>
        "No NTFS file identifier is read (no native interop). Replacement is claimed only from an "
        + "observed disappear-then-reappear sequence or a changed creation timestamp without continuous "
        + "same-account, same-path append growth. NTFS file-system tunneling can preserve the creation "
        + "timestamp of a file recreated under the same name within roughly fifteen seconds, so an "
        + "unobserved rapid recreation may appear as truncation or continuous growth.";
}

public sealed record LogActivityAccountDiagnostics
{
    public required string AccountStableId { get; init; }

    public required string AccountDisplayName { get; init; }

    public required bool LogsFolderPresent { get; init; }

    public required string RedactedLogsFolderPath { get; init; }

    public required int CandidateCount { get; init; }

    /// <summary>Why the Logs folder could not be enumerated, when enumeration failed.</summary>
    public string? FailureReason { get; init; }
}

public sealed record LogActivitySourceDiagnostics
{
    public required string SourceId { get; init; }

    public required int IdentityGeneration { get; init; }

    public required string AccountDisplayName { get; init; }

    public required string FileName { get; init; }

    public required string RedactedFilePath { get; init; }

    public required DateOnly LogDate { get; init; }

    public required bool Exists { get; init; }

    public required long PreviousLength { get; init; }

    public required long CurrentLength { get; init; }

    public required DateTimeOffset FirstObservedAt { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }

    public DateTimeOffset? FirstGrowthAt { get; init; }

    public DateTimeOffset? LastGrowthAt { get; init; }

    public required LogSourceActivityState ActivityState { get; init; }

    public required LogSourceChangeKind LastChangeKind { get; init; }

    public required bool IsCurrentDailyFile { get; init; }

    public required bool IsTruncated { get; init; }

    public required bool IsReplaced { get; init; }

    public string? ReplacementEvidence { get; init; }

    public required bool IsRolloverCandidate { get; init; }

    public string? RolloverReason { get; init; }

    public string? UnavailableReason { get; init; }
}

internal static class DiagnosticPathRedactor
{
    public static string Redact(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string profile;
        try
        {
            profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        catch
        {
            return path;
        }

        if (string.IsNullOrWhiteSpace(profile)
            || !path.StartsWith(profile, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return string.Concat("%USERPROFILE%", path.AsSpan(profile.Length));
    }
}

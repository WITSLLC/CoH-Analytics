using CoHAnalytics.Models;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Builds hand-crafted <see cref="LogSourceCandidate"/> and <see cref="LogActivitySnapshot"/>
/// instances for Slice 5B manager tests, without any real filesystem observation.
/// </summary>
internal static class TestLogCandidates
{
    public static LogSourceId SourceId(
        string accountStableId = "acct-1",
        string accountDisplayName = "Alpha",
        DateOnly? logDate = null,
        int identityGeneration = 0,
        string fileNameSuffix = "") =>
        LogSourceId.Create(
            accountStableId,
            accountDisplayName,
            $@"C:\fake\{accountStableId}\Logs\chatlog {(logDate ?? new DateOnly(2026, 8, 4)):yyyy-MM-dd}{fileNameSuffix}.txt",
            logDate ?? new DateOnly(2026, 8, 4),
            identityGeneration);

    public static LogSourceCandidate Create(
        LogSourceId sourceId,
        LogSourceActivityState activityState,
        DateTimeOffset observedAt,
        bool isRolloverCandidate = false,
        LogSourceId? rolloverPredecessor = null,
        bool exists = true,
        LogSourceChangeKind? lastChangeKind = null,
        bool isTruncated = false,
        bool isReplaced = false,
        string? replacementEvidence = null,
        DateTimeOffset? firstGrowthAt = null,
        DateTimeOffset? lastGrowthAt = null,
        long? length = null,
        long? previousLength = null)
    {
        var hasGrowth = activityState is LogSourceActivityState.Growing or LogSourceActivityState.Inactive;
        var resolvedLastGrowthAt = lastGrowthAt ?? (hasGrowth ? observedAt : null);
        var resolvedFirstGrowthAt = firstGrowthAt ?? resolvedLastGrowthAt;

        return new LogSourceCandidate
        {
            SourceId = sourceId,
            AccountStableId = sourceId.AccountStableId,
            AccountDisplayName = sourceId.AccountDisplayName,
            FilePath = sourceId.FilePath,
            FileName = sourceId.FileName,
            LogDate = sourceId.LogDate,
            Exists = exists,
            Length = exists ? length ?? 100 : 0,
            PreviousLength = exists ? previousLength ?? 50 : 0,
            LastWriteTime = exists ? observedAt : null,
            CreationTime = exists ? observedAt : null,
            FirstObservedAt = observedAt,
            LastObservedAt = observedAt,
            FirstGrowthAt = resolvedFirstGrowthAt,
            LastGrowthAt = resolvedLastGrowthAt,
            ActivityState = activityState,
            LastChangeKind = lastChangeKind ?? (activityState == LogSourceActivityState.Growing
                ? LogSourceChangeKind.Grew
                : LogSourceChangeKind.Unchanged),
            IsCurrentDailyFile = sourceId.LogDate == DateOnly.FromDateTime(observedAt.DateTime),
            IsRolloverCandidate = isRolloverCandidate,
            RolloverPredecessorSourceId = rolloverPredecessor?.Value,
            RolloverReason = isRolloverCandidate ? "Same-account daily rollover." : null,
            IsTruncated = isTruncated,
            IsReplaced = isReplaced,
            ReplacementEvidence = replacementEvidence
        };
    }

    public static LogActivitySnapshot Snapshot(DateTimeOffset observedAt, params LogSourceCandidate[] candidates) =>
        Snapshot(revision: 1, observedAt, candidates);

    public static LogActivitySnapshot Snapshot(
        long revision,
        DateTimeOffset observedAt,
        params LogSourceCandidate[] candidates) =>
        LogActivitySnapshot.Create(
            candidates,
            observedAccountCount: candidates.Select(c => c.AccountStableId).Distinct().Count(),
            logsFolderCount: candidates.Select(c => c.AccountStableId).Distinct().Count(),
            observedAt,
            revision);
}

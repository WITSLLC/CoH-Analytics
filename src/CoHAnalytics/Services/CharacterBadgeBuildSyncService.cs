using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Services;

/// <summary>
/// User-triggered additive sync of Homecoming <c>Badges Earned:</c> source IDs into the
/// per-character badge acquisition store.
/// </summary>
public sealed class CharacterBadgeBuildSyncService
{
    private readonly ICharacterRepository _characterRepository;
    private readonly ICharacterBadgeAcquisitionRepository _badgeAcquisitionRepository;
    private readonly IItemReferenceCatalog _itemReferenceCatalog;
    private readonly TimeProvider _timeProvider;

    public CharacterBadgeBuildSyncService(
        ICharacterRepository characterRepository,
        ICharacterBadgeAcquisitionRepository badgeAcquisitionRepository,
        IItemReferenceCatalog itemReferenceCatalog,
        TimeProvider? timeProvider = null)
    {
        _characterRepository = characterRepository;
        _badgeAcquisitionRepository = badgeAcquisitionRepository;
        _itemReferenceCatalog = itemReferenceCatalog;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public CharacterBadgeBuildSyncResult SyncFromBuild(
        string accountStableId,
        string accountFolderPath,
        CharacterRecordId recordId)
    {
        var record = _characterRepository.TryGetRecord(recordId);
        if (record is null)
        {
            return CharacterBadgeBuildSyncResult.Failed("Character record was not found.");
        }

        if (!string.Equals(record.AccountStableId, accountStableId, StringComparison.Ordinal))
        {
            return CharacterBadgeBuildSyncResult.Failed(
                "Character record does not belong to the requested account.");
        }

        if (!CharacterShortId.TryParse(record.CharacterShortId, out var shortId))
        {
            return CharacterBadgeBuildSyncResult.Failed("Character short id is not available.");
        }

        var buildPath = CharacterBuildImportService.GetBuildSaveFilePath(accountFolderPath, shortId);
        if (!File.Exists(buildPath))
        {
            return CharacterBadgeBuildSyncResult.MissingFile();
        }

        string content;
        try
        {
            content = File.ReadAllText(buildPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CharacterBadgeBuildSyncResult.Failed(
                $"Build file could not be read: {ex.GetType().Name}.");
        }

        if (!HomecomingBuildSaveMetadataParser.TryParseBadgeSourceIds(content, out var sourceIds))
        {
            return CharacterBadgeBuildSyncResult.NoBadgeSection();
        }

        if (sourceIds.Count == 0)
        {
            return CharacterBadgeBuildSyncResult.Completed(0, 0, 0);
        }

        var added = 0;
        var alreadyTracked = 0;
        var unrecognized = 0;
        var observedAt = _timeProvider.GetUtcNow();

        foreach (var sourceId in sourceIds)
        {
            if (!_itemReferenceCatalog.TryGetBadgeByHomecomingSourceId(sourceId, out var badge))
            {
                unrecognized++;
                continue;
            }

            if (_badgeAcquisitionRepository.IsBadgeAcquired(recordId, badge.CatalogItemId))
            {
                alreadyTracked++;
                continue;
            }

            var result = _badgeAcquisitionRepository.RecordAcquisition(
                recordId,
                accountStableId,
                badge.CatalogItemId,
                badge.HeroName,
                observedAt);

            if (!result.IsSuccess)
            {
                return CharacterBadgeBuildSyncResult.Failed(
                    result.FailureReason ?? "Badge acquisition could not be persisted.");
            }

            added++;
        }

        return CharacterBadgeBuildSyncResult.Completed(added, alreadyTracked, unrecognized);
    }
}

public enum CharacterBadgeBuildSyncStatus
{
    Completed,
    MissingFile,
    NoBadgeSection,
    Failed
}

public sealed record CharacterBadgeBuildSyncResult
{
    public CharacterBadgeBuildSyncStatus Status { get; init; }

    public int AddedCount { get; init; }

    public int AlreadyTrackedCount { get; init; }

    public int UnrecognizedCount { get; init; }

    public string? Message { get; init; }

    public string FormatUserMessage() =>
        Status switch
        {
            CharacterBadgeBuildSyncStatus.MissingFile =>
                "No build file was found for this character. Save or export the character build in Homecoming, then try again.",
            CharacterBadgeBuildSyncStatus.NoBadgeSection =>
                "No badge data was found in the current build.",
            CharacterBadgeBuildSyncStatus.Failed =>
                Message ?? "Badge sync could not be completed.",
            _ when AddedCount == 0 && AlreadyTrackedCount == 0 && UnrecognizedCount == 0 =>
                "No badge data was found in the current build.",
            _ =>
                $"{AddedCount} badges added • {AlreadyTrackedCount} already tracked • {UnrecognizedCount} unrecognized"
        };

    public static CharacterBadgeBuildSyncResult Completed(
        int added,
        int alreadyTracked,
        int unrecognized) =>
        new()
        {
            Status = CharacterBadgeBuildSyncStatus.Completed,
            AddedCount = added,
            AlreadyTrackedCount = alreadyTracked,
            UnrecognizedCount = unrecognized
        };

    public static CharacterBadgeBuildSyncResult MissingFile() =>
        new() { Status = CharacterBadgeBuildSyncStatus.MissingFile };

    public static CharacterBadgeBuildSyncResult NoBadgeSection() =>
        new() { Status = CharacterBadgeBuildSyncStatus.NoBadgeSection };

    public static CharacterBadgeBuildSyncResult Failed(string message) =>
        new()
        {
            Status = CharacterBadgeBuildSyncStatus.Failed,
            Message = message
        };
}

using System.IO;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Reads and parses the current Homecoming buildsave layout for one trusted character.</summary>
public sealed class CharacterBuildLayoutSyncService
{
    private readonly ICharacterRepository _characterRepository;
    private readonly TimeProvider _timeProvider;

    public CharacterBuildLayoutSyncService(
        ICharacterRepository characterRepository,
        TimeProvider? timeProvider = null)
    {
        _characterRepository = characterRepository;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public CharacterBuildLayoutSyncResult SyncFromBuild(
        string accountStableId,
        string accountFolderPath,
        CharacterRecordId recordId)
    {
        var record = _characterRepository.TryGetRecord(recordId);
        if (record is null)
        {
            return CharacterBuildLayoutSyncResult.NotFound("Character record was not found.");
        }

        if (!string.Equals(record.AccountStableId, accountStableId, StringComparison.Ordinal))
        {
            return CharacterBuildLayoutSyncResult.Rejected(
                "Character record does not belong to the requested account.");
        }

        if (!CharacterShortId.TryParse(record.CharacterShortId, out var shortId))
        {
            return CharacterBuildLayoutSyncResult.Rejected("Character short id is not available.");
        }

        var buildFilePath = CharacterBuildImportService.GetBuildSaveFilePath(accountFolderPath, shortId);
        if (!File.Exists(buildFilePath))
        {
            return CharacterBuildLayoutSyncResult.MissingFile(buildFilePath);
        }

        string content;
        try
        {
            content = File.ReadAllText(buildFilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CharacterBuildLayoutSyncResult.Failed(
                $"Build file could not be read: {exception.GetType().Name}.",
                buildFilePath);
        }

        if (!HomecomingBuildLayoutParser.TryParse(content, out var snapshot))
        {
            return CharacterBuildLayoutSyncResult.Failed(
                "Build file format is malformed or unsupported.",
                buildFilePath);
        }

        return CharacterBuildLayoutSyncResult.Success(
            snapshot,
            buildFilePath,
            _timeProvider.GetUtcNow(),
            TryGetLastWriteTimeUtc(buildFilePath));
    }

    private static DateTimeOffset? TryGetLastWriteTimeUtc(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

public sealed record CharacterBuildLayoutSyncResult
{
    public CharacterBuildLayoutSyncStatus Status { get; init; }

    public HomecomingBuildLayoutSnapshot? Snapshot { get; init; }

    public string? BuildFilePath { get; init; }

    public DateTimeOffset? SyncedAt { get; init; }

    public DateTimeOffset? SourceLastWriteUtc { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess => Status == CharacterBuildLayoutSyncStatus.Synced;

    public static CharacterBuildLayoutSyncResult Success(
        HomecomingBuildLayoutSnapshot snapshot,
        string buildFilePath,
        DateTimeOffset syncedAt,
        DateTimeOffset? sourceLastWriteUtc = null) =>
        new()
        {
            Status = CharacterBuildLayoutSyncStatus.Synced,
            Snapshot = snapshot,
            BuildFilePath = buildFilePath,
            SyncedAt = syncedAt,
            SourceLastWriteUtc = sourceLastWriteUtc
        };

    public static CharacterBuildLayoutSyncResult MissingFile(string buildFilePath) =>
        new()
        {
            Status = CharacterBuildLayoutSyncStatus.MissingFile,
            BuildFilePath = buildFilePath
        };

    public static CharacterBuildLayoutSyncResult NotFound(string detail) =>
        new() { Status = CharacterBuildLayoutSyncStatus.NotFound, Detail = detail };

    public static CharacterBuildLayoutSyncResult Rejected(string detail) =>
        new() { Status = CharacterBuildLayoutSyncStatus.Rejected, Detail = detail };

    public static CharacterBuildLayoutSyncResult Failed(string detail, string? buildFilePath = null) =>
        new()
        {
            Status = CharacterBuildLayoutSyncStatus.Failed,
            Detail = detail,
            BuildFilePath = buildFilePath
        };
}

public enum CharacterBuildLayoutSyncStatus
{
    Synced,
    MissingFile,
    NotFound,
    Rejected,
    Failed
}

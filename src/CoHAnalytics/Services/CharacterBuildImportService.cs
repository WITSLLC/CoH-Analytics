using System.IO;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Imports build metadata from account-scoped Homecoming <c>Builds</c> files keyed by
/// <see cref="CharacterShortId"/>.
/// </summary>
public sealed class CharacterBuildImportService
{
    private readonly ICharacterRepository _characterRepository;
    private readonly IHomecomingArchetypePowerReferenceCatalog? _powerCatalog;
    private readonly TimeProvider _timeProvider;
    private readonly ParserManagerOptions _parserOptions;

    public CharacterBuildImportService(
        ICharacterRepository characterRepository,
        IHomecomingArchetypePowerReferenceCatalog? powerCatalog = null,
        ParserManagerOptions? parserOptions = null,
        TimeProvider? timeProvider = null)
    {
        _characterRepository = characterRepository;
        _powerCatalog = powerCatalog;
        _parserOptions = parserOptions ?? new ParserManagerOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static string GetBuildSaveFilePath(string accountFolderPath, CharacterShortId shortId) =>
        Path.Combine(accountFolderPath, "Builds", CharacterShortId.GetBuildFileName(shortId));

    public CharacterBuildImportResult TryImportIfPresent(
        string accountStableId,
        string accountFolderPath,
        CharacterRecordId recordId,
        string? accountLogFilePath = null)
    {
        var record = _characterRepository.TryGetRecord(recordId);
        if (record is null)
        {
            return CharacterBuildImportResult.NotFound("Character record was not found.");
        }

        if (!string.Equals(record.AccountStableId, accountStableId, StringComparison.Ordinal))
        {
            return CharacterBuildImportResult.Rejected("Character record does not belong to the requested account.");
        }

        if (!CharacterShortId.TryParse(record.CharacterShortId, out var shortId))
        {
            return CharacterBuildImportResult.Rejected("Character short id is not available.");
        }

        var buildPath = GetBuildSaveFilePath(accountFolderPath, shortId);
        if (!File.Exists(buildPath))
        {
            return CharacterBuildImportResult.MissingFile(buildPath);
        }

        return ImportFromBuildFile(accountStableId, accountFolderPath, recordId, buildPath, accountLogFilePath);
    }

    public CharacterBuildImportResult ImportFromBuildFile(
        string accountStableId,
        string accountFolderPath,
        CharacterRecordId recordId,
        string buildFilePath,
        string? accountLogFilePath = null)
    {
        var record = _characterRepository.TryGetRecord(recordId);
        if (record is null)
        {
            return CharacterBuildImportResult.NotFound("Character record was not found.");
        }

        if (!string.Equals(record.AccountStableId, accountStableId, StringComparison.Ordinal))
        {
            return CharacterBuildImportResult.Rejected("Character record does not belong to the requested account.");
        }

        if (!CharacterShortId.TryParse(record.CharacterShortId, out var shortId))
        {
            return CharacterBuildImportResult.Rejected("Character short id is not available.");
        }

        var expectedPath = Path.GetFullPath(GetBuildSaveFilePath(accountFolderPath, shortId));
        var normalizedBuildPath = Path.GetFullPath(buildFilePath);
        if (!string.Equals(expectedPath, normalizedBuildPath, StringComparison.OrdinalIgnoreCase))
        {
            return CharacterBuildImportResult.Rejected(
                "Build file path does not match the account Builds folder for this character short id.");
        }

        if (!File.Exists(normalizedBuildPath))
        {
            return CharacterBuildImportResult.MissingFile(normalizedBuildPath);
        }

        string content;
        try
        {
            content = File.ReadAllText(normalizedBuildPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CharacterBuildImportResult.Failed($"Build file could not be read: {ex.GetType().Name}.");
        }

        if (!HomecomingBuildSaveMetadataParser.TryParse(content, _powerCatalog, out var metadata))
        {
            return CharacterBuildImportResult.Failed("Build file format is not supported for metadata import.");
        }

        var observedAt = _timeProvider.GetUtcNow();
        var metadataResult = _characterRepository.ImportBuildMetadata(
            recordId,
            metadata.PrimaryPowerSet,
            metadata.SecondaryPowerSet,
            metadata.Archetype,
            metadata.CurrentBuildNumber,
            observedAt);

        if (!metadataResult.IsSuccess)
        {
            return CharacterBuildImportResult.Failed(metadataResult.Detail ?? "Build metadata could not be persisted.");
        }

        var welcomeName = TryFindTrustedWelcomeName(accountStableId, accountFolderPath, accountLogFilePath);
        if (welcomeName is not null
            && CharacterNameNormalizer.IsValidDisplayName(welcomeName)
            && !CharacterNameNormalizer.NamesMatch(
                CharacterNameNormalizer.Normalize(record.CurrentDisplayName),
                CharacterNameNormalizer.Normalize(welcomeName)))
        {
            var renameResult = _characterRepository.RecordTrustedObservedDisplayName(
                recordId,
                welcomeName,
                CharacterTrustState.TrustedFromWelcome);
            if (!renameResult.IsSuccess)
            {
                return CharacterBuildImportResult.Failed(renameResult.Detail ?? "Welcome rename could not be applied.");
            }
        }

        return CharacterBuildImportResult.Success(metadataResult.RecordId!, metadata, normalizedBuildPath);
    }

    private string? TryFindTrustedWelcomeName(
        string accountStableId,
        string accountFolderPath,
        string? accountLogFilePath)
    {
        var logPath = ResolveAccountLogPath(accountStableId, accountFolderPath, accountLogFilePath);
        if (logPath is null || !File.Exists(logPath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                logPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var observedAt = _timeProvider.GetUtcNow();
            var sourceId = LogSourceId.Create(
                accountStableId,
                Path.GetFileName(Path.GetDirectoryName(logPath) ?? accountFolderPath),
                logPath,
                DateOnly.FromDateTime(observedAt.DateTime));
            var sequence = 0L;
            var welcome = ParserStartupIdentityRecovery.FindMostRecentWelcomeBeforeOffset(
                stream,
                stream.Length,
                _parserOptions.ReadBufferSize,
                MonitoringContextId.CreateNew(),
                sourceId,
                ParserSourceSegmentId.CreateNew(),
                1,
                MonitoringSourceTransitionKind.SourceAssigned,
                observedAt,
                _parserOptions.MaximumLineBytes,
                ref sequence);

            if (welcome is null)
            {
                return null;
            }

            var classified = new ParserClassifier().Classify(welcome);
            return CharacterIdentityResolver.GetStrongCandidateName(classified);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ResolveAccountLogPath(
        string accountStableId,
        string accountFolderPath,
        string? accountLogFilePath)
    {
        if (accountLogFilePath is not null)
        {
            var normalized = Path.GetFullPath(accountLogFilePath);
            var logsFolder = Path.GetFullPath(Path.Combine(accountFolderPath, "Logs"));
            if (!normalized.StartsWith(logsFolder, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return normalized;
        }

        var logsDirectory = Path.Combine(accountFolderPath, "Logs");
        if (!Directory.Exists(logsDirectory))
        {
            return null;
        }

        string? newestPath = null;
        DateTimeOffset? newestTimestamp = null;
        foreach (var candidate in Directory.EnumerateFiles(logsDirectory, "chatlog *.txt"))
        {
            var info = new FileInfo(candidate);
            var timestamp = info.LastWriteTimeUtc;
            if (newestTimestamp is null || timestamp > newestTimestamp.Value)
            {
                newestTimestamp = timestamp;
                newestPath = candidate;
            }
        }

        return newestPath;
    }
}

public sealed record CharacterBuildImportResult
{
    public CharacterBuildImportStatus Status { get; init; }

    public CharacterRecordId? RecordId { get; init; }

    public HomecomingBuildSaveMetadata? Metadata { get; init; }

    public string? BuildFilePath { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess => Status == CharacterBuildImportStatus.Imported;

    public static CharacterBuildImportResult Success(
        CharacterRecordId recordId,
        HomecomingBuildSaveMetadata metadata,
        string buildFilePath) =>
        new()
        {
            Status = CharacterBuildImportStatus.Imported,
            RecordId = recordId,
            Metadata = metadata,
            BuildFilePath = buildFilePath
        };

    public static CharacterBuildImportResult MissingFile(string buildFilePath) =>
        new()
        {
            Status = CharacterBuildImportStatus.MissingFile,
            BuildFilePath = buildFilePath
        };

    public static CharacterBuildImportResult NotFound(string detail) =>
        new() { Status = CharacterBuildImportStatus.NotFound, Detail = detail };

    public static CharacterBuildImportResult Rejected(string detail) =>
        new() { Status = CharacterBuildImportStatus.Rejected, Detail = detail };

    public static CharacterBuildImportResult Failed(string detail) =>
        new() { Status = CharacterBuildImportStatus.Failed, Detail = detail };
}

public enum CharacterBuildImportStatus
{
    Imported,
    MissingFile,
    NotFound,
    Rejected,
    Failed
}

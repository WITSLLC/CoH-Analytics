using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Persists independent, versioned build-layout snapshots by stable character record id.</summary>
public sealed class CharacterBuildSnapshotStore : ICharacterBuildSnapshotStore
{
    public const int PersistenceSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly CharacterBuildSnapshotStoreOptions _options;
    private readonly string _snapshotDirectory;
    private readonly object _sync = new();

    public CharacterBuildSnapshotStore(CharacterBuildSnapshotStoreOptions? options = null)
    {
        _options = options ?? new CharacterBuildSnapshotStoreOptions();
        _snapshotDirectory = ApplicationDataPaths.GetBuildSnapshotsDirectory(_options.DataDirectory);
    }

    public CharacterBuildSnapshotLoadResult TryLoad(CharacterRecordId characterRecordId)
    {
        ArgumentNullException.ThrowIfNull(characterRecordId);
        var path = GetSnapshotPath(characterRecordId);

        lock (_sync)
        {
            if (!File.Exists(path))
            {
                return CharacterBuildSnapshotLoadResult.NotFound(path);
            }

            try
            {
                using var stream = File.OpenRead(path);
                var document = JsonSerializer.Deserialize<PersistedBuildSnapshotDocument>(
                    stream,
                    SerializerOptions);
                if (document is null)
                {
                    return CharacterBuildSnapshotLoadResult.Corrupt(path, "Snapshot document is empty.");
                }

                if (document.SchemaVersion != PersistenceSchemaVersion)
                {
                    return CharacterBuildSnapshotLoadResult.UnsupportedSchema(
                        path,
                        document.SchemaVersion);
                }

                if (!Guid.TryParse(document.CharacterRecordId, out var persistedGuid)
                    || CharacterRecordId.FromGuid(persistedGuid) != characterRecordId)
                {
                    return CharacterBuildSnapshotLoadResult.Corrupt(
                        path,
                        "Snapshot character record id does not match its owner.");
                }

                if (!TryRestoreLayout(document.Snapshot, out var layout, out var detail))
                {
                    return CharacterBuildSnapshotLoadResult.Corrupt(path, detail);
                }

                return CharacterBuildSnapshotLoadResult.Loaded(
                    path,
                    new CharacterBuildSnapshot
                    {
                        CharacterRecordId = characterRecordId,
                        CharacterShortId = document.ShortId,
                        CharacterName = document.CharacterName,
                        SyncedAtUtc = document.SyncedAtUtc,
                        SourceBuildFile = document.SourceBuildFile,
                        SourceLastWriteUtc = document.SourceLastWriteUtc,
                        Layout = layout!
                    });
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or JsonException
                                               or NotSupportedException)
            {
                return CharacterBuildSnapshotLoadResult.Corrupt(
                    path,
                    $"Snapshot could not be loaded: {exception.GetType().Name}.");
            }
        }
    }

    public CharacterBuildSnapshotSaveResult Save(CharacterBuildSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.CharacterRecordId);
        ArgumentNullException.ThrowIfNull(snapshot.Layout);

        var path = GetSnapshotPath(snapshot.CharacterRecordId);
        var tempPath = Path.Combine(
            _snapshotDirectory,
            $".{snapshot.CharacterRecordId}.{Guid.NewGuid():n}.tmp");

        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(_snapshotDirectory);
                WriteTemporaryFile(tempPath, ToDocument(snapshot));
                (_options.AtomicReplace ?? ReplaceFile)(tempPath, path);
                return CharacterBuildSnapshotSaveResult.Saved(path);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or JsonException
                                               or NotSupportedException)
            {
                return CharacterBuildSnapshotSaveResult.Failed(
                    path,
                    $"Snapshot could not be saved: {exception.GetType().Name}.");
            }
            finally
            {
                TryDeleteTemporaryFile(tempPath);
            }
        }
    }

    internal string GetSnapshotPath(CharacterRecordId characterRecordId) =>
        Path.Combine(_snapshotDirectory, $"{characterRecordId}.json");

    private static PersistedBuildSnapshotDocument ToDocument(CharacterBuildSnapshot snapshot) =>
        new()
        {
            SchemaVersion = PersistenceSchemaVersion,
            CharacterRecordId = snapshot.CharacterRecordId.ToString(),
            ShortId = snapshot.CharacterShortId,
            CharacterName = snapshot.CharacterName,
            SyncedAtUtc = snapshot.SyncedAtUtc,
            SourceBuildFile = snapshot.SourceBuildFile,
            SourceLastWriteUtc = snapshot.SourceLastWriteUtc,
            Snapshot = new PersistedBuildLayout
            {
                CharacterName = snapshot.Layout.CharacterName,
                CharacterLevel = snapshot.Layout.CharacterLevel,
                RawClassToken = snapshot.Layout.RawClassToken,
                Powers = snapshot.Layout.Powers.Select(power => new PersistedBuildPower
                {
                    AcquisitionLevel = power.AcquisitionLevel,
                    RawCategoryToken = power.RawCategoryToken,
                    RawPowerSetToken = power.RawPowerSetToken,
                    RawPowerToken = power.RawPowerToken,
                    SourceOrder = power.SourceOrder,
                    Slots = power.Slots.Select(slot => new PersistedBuildSlot
                    {
                        IsEmpty = slot.IsEmpty,
                        RawEnhancementToken = slot.RawEnhancementToken,
                        IsAttuned = slot.IsAttuned,
                        BaseEnhancementLevel = slot.BaseEnhancementLevel,
                        BoostValue = slot.BoostValue,
                        SlotOrder = slot.SlotOrder
                    }).ToList()
                }).ToList()
            }
        };

    private static bool TryRestoreLayout(
        PersistedBuildLayout? persisted,
        out HomecomingBuildLayoutSnapshot? layout,
        out string detail)
    {
        layout = null;
        detail = "Snapshot layout is missing or invalid.";
        if (persisted?.Powers is null)
        {
            return false;
        }

        var powers = new List<HomecomingBuildPowerSnapshot>(persisted.Powers.Count);
        foreach (var power in persisted.Powers)
        {
            if (power is null
                || power.AcquisitionLevel < 0
                || power.SourceOrder < 0
                || string.IsNullOrWhiteSpace(power.RawCategoryToken)
                || string.IsNullOrWhiteSpace(power.RawPowerSetToken)
                || string.IsNullOrWhiteSpace(power.RawPowerToken)
                || power.Slots is null)
            {
                return false;
            }

            var slots = new List<HomecomingBuildSlotSnapshot>(power.Slots.Count);
            foreach (var slot in power.Slots)
            {
                if (slot is null
                    || slot.SlotOrder < 0
                    || (!slot.IsEmpty && string.IsNullOrWhiteSpace(slot.RawEnhancementToken)))
                {
                    return false;
                }

                slots.Add(new HomecomingBuildSlotSnapshot(
                    slot.IsEmpty,
                    slot.RawEnhancementToken,
                    slot.IsAttuned,
                    slot.BaseEnhancementLevel,
                    slot.BoostValue,
                    slot.SlotOrder));
            }

            powers.Add(new HomecomingBuildPowerSnapshot(
                power.AcquisitionLevel,
                power.RawCategoryToken,
                power.RawPowerSetToken,
                power.RawPowerToken,
                power.SourceOrder,
                slots));
        }

        layout = new HomecomingBuildLayoutSnapshot(
            persisted.CharacterName,
            persisted.CharacterLevel,
            persisted.RawClassToken,
            powers);
        detail = string.Empty;
        return true;
    }

    private static void WriteTemporaryFile(string path, PersistedBuildSnapshotDocument document)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, document, SerializerOptions);
        stream.Flush(flushToDisk: true);
    }

    private static void ReplaceFile(string tempPath, string finalPath) =>
        File.Move(tempPath, finalPath, overwrite: true);

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed class PersistedBuildSnapshotDocument
    {
        public int SchemaVersion { get; init; }

        public string? CharacterRecordId { get; init; }

        public string? ShortId { get; init; }

        public string? CharacterName { get; init; }

        public DateTimeOffset SyncedAtUtc { get; init; }

        public string? SourceBuildFile { get; init; }

        public DateTimeOffset? SourceLastWriteUtc { get; init; }

        public PersistedBuildLayout? Snapshot { get; init; }
    }

    private sealed class PersistedBuildLayout
    {
        public string? CharacterName { get; init; }

        public int? CharacterLevel { get; init; }

        public string? RawClassToken { get; init; }

        public List<PersistedBuildPower>? Powers { get; init; }
    }

    private sealed class PersistedBuildPower
    {
        public int AcquisitionLevel { get; init; }

        public string? RawCategoryToken { get; init; }

        public string? RawPowerSetToken { get; init; }

        public string? RawPowerToken { get; init; }

        public int SourceOrder { get; init; }

        public List<PersistedBuildSlot>? Slots { get; init; }
    }

    private sealed class PersistedBuildSlot
    {
        public bool IsEmpty { get; init; }

        public string? RawEnhancementToken { get; init; }

        public bool IsAttuned { get; init; }

        public int? BaseEnhancementLevel { get; init; }

        public int? BoostValue { get; init; }

        public int SlotOrder { get; init; }
    }
}

public interface ICharacterBuildSnapshotStore
{
    CharacterBuildSnapshotLoadResult TryLoad(CharacterRecordId characterRecordId);

    CharacterBuildSnapshotSaveResult Save(CharacterBuildSnapshot snapshot);
}

internal sealed class NullCharacterBuildSnapshotStore : ICharacterBuildSnapshotStore
{
    public static NullCharacterBuildSnapshotStore Instance { get; } = new();

    private NullCharacterBuildSnapshotStore()
    {
    }

    public CharacterBuildSnapshotLoadResult TryLoad(CharacterRecordId characterRecordId) =>
        CharacterBuildSnapshotLoadResult.NotFound(string.Empty);

    public CharacterBuildSnapshotSaveResult Save(CharacterBuildSnapshot snapshot) =>
        CharacterBuildSnapshotSaveResult.Saved(string.Empty);
}

public sealed class CharacterBuildSnapshotStoreOptions
{
    public string? DataDirectory { get; init; }

    internal Action<string, string>? AtomicReplace { get; init; }
}

public enum CharacterBuildSnapshotLoadOutcome
{
    Loaded,
    NotFound,
    Corrupt,
    UnsupportedSchema
}

public sealed record CharacterBuildSnapshotLoadResult
{
    public required CharacterBuildSnapshotLoadOutcome Outcome { get; init; }

    public required string Path { get; init; }

    public CharacterBuildSnapshot? Snapshot { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess => Outcome == CharacterBuildSnapshotLoadOutcome.Loaded;

    internal static CharacterBuildSnapshotLoadResult Loaded(
        string path,
        CharacterBuildSnapshot snapshot) =>
        new() { Outcome = CharacterBuildSnapshotLoadOutcome.Loaded, Path = path, Snapshot = snapshot };

    internal static CharacterBuildSnapshotLoadResult NotFound(string path) =>
        new() { Outcome = CharacterBuildSnapshotLoadOutcome.NotFound, Path = path };

    internal static CharacterBuildSnapshotLoadResult Corrupt(string path, string detail) =>
        new() { Outcome = CharacterBuildSnapshotLoadOutcome.Corrupt, Path = path, Detail = detail };

    internal static CharacterBuildSnapshotLoadResult UnsupportedSchema(string path, int schemaVersion) =>
        new()
        {
            Outcome = CharacterBuildSnapshotLoadOutcome.UnsupportedSchema,
            Path = path,
            Detail = $"Snapshot schema version {schemaVersion} is not supported."
        };
}

public enum CharacterBuildSnapshotSaveOutcome
{
    Saved,
    Failed
}

public sealed record CharacterBuildSnapshotSaveResult
{
    public required CharacterBuildSnapshotSaveOutcome Outcome { get; init; }

    public required string Path { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess => Outcome == CharacterBuildSnapshotSaveOutcome.Saved;

    internal static CharacterBuildSnapshotSaveResult Saved(string path) =>
        new() { Outcome = CharacterBuildSnapshotSaveOutcome.Saved, Path = path };

    internal static CharacterBuildSnapshotSaveResult Failed(string path, string detail) =>
        new() { Outcome = CharacterBuildSnapshotSaveOutcome.Failed, Path = path, Detail = detail };
}

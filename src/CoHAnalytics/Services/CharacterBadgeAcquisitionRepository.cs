using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Persistent per-character badge acquisition store with atomic JSON saves.
/// </summary>
public sealed class CharacterBadgeAcquisitionRepository : ICharacterBadgeAcquisitionRepository
{
    public const int PersistenceSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly CharacterBadgeAcquisitionRepositoryOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly string _dataDirectory;
    private readonly string _repositoryPath;
    private readonly object _sync = new();
    private readonly Dictionary<CharacterRecordId, MutableCharacterState> _characters = [];

    public CharacterBadgeAcquisitionRepository(CharacterBadgeAcquisitionRepositoryOptions? options = null)
    {
        _options = options ?? new CharacterBadgeAcquisitionRepositoryOptions();
        _timeProvider = _options.TimeProvider ?? TimeProvider.System;
        _dataDirectory = ApplicationDataPaths.GetCharactersRoot(_options.DataDirectory);
        _repositoryPath = ApplicationDataPaths.GetBadgeAcquisitionsPath(_options.DataDirectory);
        LoadLocked();
    }

    public CharacterBadgeAcquisitionSnapshot GetSnapshot(CharacterRecordId characterRecordId)
    {
        lock (_sync)
        {
            if (!_characters.TryGetValue(characterRecordId, out var state))
            {
                return CharacterBadgeAcquisitionSnapshot.Empty with
                {
                    CharacterRecordId = characterRecordId
                };
            }

            return ToSnapshot(characterRecordId, state);
        }
    }

    public bool IsBadgeAcquired(CharacterRecordId characterRecordId, string catalogItemId)
    {
        if (string.IsNullOrWhiteSpace(catalogItemId))
        {
            return false;
        }

        lock (_sync)
        {
            return _characters.TryGetValue(characterRecordId, out var state)
                && state.Acquisitions.ContainsKey(catalogItemId);
        }
    }

    public IReadOnlyCollection<string> GetAcquiredBadgeIds(CharacterRecordId characterRecordId)
    {
        lock (_sync)
        {
            if (!_characters.TryGetValue(characterRecordId, out var state))
            {
                return Array.Empty<string>();
            }

            return state.Acquisitions.Keys
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public CharacterBadgeAcquisitionOperationResult RecordAcquisition(
        CharacterRecordId characterRecordId,
        string accountStableId,
        string catalogItemId,
        string observedTitle,
        DateTimeOffset observedAt) =>
        RecordAcquisition(
            characterRecordId,
            accountStableId,
            catalogItemId,
            observedTitle,
            observedAt,
            CharacterBadgeAcquisitionProvenance.LegacyUnknown);

    public CharacterBadgeAcquisitionOperationResult RecordAcquisition(
        CharacterRecordId characterRecordId,
        string accountStableId,
        string catalogItemId,
        string observedTitle,
        DateTimeOffset observedAt,
        CharacterBadgeAcquisitionProvenance provenance)
    {
        if (string.IsNullOrWhiteSpace(accountStableId)
            || string.IsNullOrWhiteSpace(catalogItemId)
            || string.IsNullOrWhiteSpace(observedTitle))
        {
            return CharacterBadgeAcquisitionOperationResult.Failure(
                CharacterBadgeAcquisitionOutcome.InvalidArgument,
                "Character badge acquisition requires account, badge id, and observed title.");
        }

        lock (_sync)
        {
            if (!_characters.TryGetValue(characterRecordId, out var state))
            {
                state = new MutableCharacterState { AccountStableId = accountStableId };
                _characters[characterRecordId] = state;
            }
            else if (!string.Equals(state.AccountStableId, accountStableId, StringComparison.Ordinal))
            {
                return CharacterBadgeAcquisitionOperationResult.Failure(
                    CharacterBadgeAcquisitionOutcome.InvalidArgument,
                    $"Character '{characterRecordId}' does not belong to account '{accountStableId}'.");
            }

            if (state.Acquisitions.TryGetValue(catalogItemId, out var existing))
            {
                if (provenance == CharacterBadgeAcquisitionProvenance.LogReceipt
                    && existing.Provenance != CharacterBadgeAcquisitionProvenance.LogReceipt)
                {
                    state.Acquisitions[catalogItemId] = existing with
                    {
                        ObservedTitle = observedTitle,
                        Provenance = CharacterBadgeAcquisitionProvenance.LogReceipt
                    };

                    if (!TryPersistLocked(observedAt))
                    {
                        state.Acquisitions[catalogItemId] = existing;
                        return CharacterBadgeAcquisitionOperationResult.Failure(
                            CharacterBadgeAcquisitionOutcome.PersistenceFailed,
                            "Badge acquisition could not be persisted.");
                    }
                }

                return CharacterBadgeAcquisitionOperationResult.Success();
            }

            state.Acquisitions[catalogItemId] = new CharacterBadgeAcquisitionEntry
            {
                CatalogItemId = catalogItemId,
                FirstObservedAt = observedAt,
                ObservedTitle = observedTitle,
                Provenance = provenance
            };

            if (!TryPersistLocked(observedAt))
            {
                state.Acquisitions.Remove(catalogItemId);
                return CharacterBadgeAcquisitionOperationResult.Failure(
                    CharacterBadgeAcquisitionOutcome.PersistenceFailed,
                    "Badge acquisition could not be persisted.");
            }

            return CharacterBadgeAcquisitionOperationResult.Success();
        }
    }

    private bool TryPersistLocked(DateTimeOffset now)
    {
        try
        {
            if (_options.SimulatePersistenceFailure)
            {
                throw new IOException("Simulated badge acquisition persistence failure.");
            }

            PersistLocked(now);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void LoadLocked()
    {
        _characters.Clear();

        if (!File.Exists(_repositoryPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_repositoryPath);
            var document = JsonSerializer.Deserialize<PersistedRepository>(json, SerializerOptions);
            if (document is null || document.SchemaVersion != PersistenceSchemaVersion)
            {
                return;
            }

            foreach (var persistedCharacter in document.Characters)
            {
                if (!Guid.TryParse(persistedCharacter.CharacterRecordId, out var recordGuid)
                    || string.IsNullOrWhiteSpace(persistedCharacter.AccountStableId))
                {
                    continue;
                }

                var recordId = CharacterRecordId.FromGuid(recordGuid);
                var state = new MutableCharacterState
                {
                    AccountStableId = persistedCharacter.AccountStableId
                };

                foreach (var acquisition in persistedCharacter.Acquisitions)
                {
                    if (string.IsNullOrWhiteSpace(acquisition.CatalogItemId)
                        || string.IsNullOrWhiteSpace(acquisition.ObservedTitle))
                    {
                        continue;
                    }

                    state.Acquisitions[acquisition.CatalogItemId] = new CharacterBadgeAcquisitionEntry
                    {
                        CatalogItemId = acquisition.CatalogItemId,
                        FirstObservedAt = acquisition.FirstObservedAt,
                        ObservedTitle = acquisition.ObservedTitle,
                        Provenance = ParseProvenance(acquisition.Provenance)
                    };
                }

                _characters[recordId] = state;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _characters.Clear();
        }
    }

    private void PersistLocked(DateTimeOffset now)
    {
        Directory.CreateDirectory(_dataDirectory);

        var document = new PersistedRepository
        {
            SchemaVersion = PersistenceSchemaVersion,
            Characters = _characters
                .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
                .Select(pair => new PersistedCharacter
                {
                    CharacterRecordId = pair.Key.ToString(),
                    AccountStableId = pair.Value.AccountStableId,
                    Acquisitions = pair.Value.Acquisitions.Values
                        .OrderBy(entry => entry.CatalogItemId, StringComparer.Ordinal)
                        .Select(entry => new PersistedAcquisition
                        {
                            CatalogItemId = entry.CatalogItemId,
                            FirstObservedAt = entry.FirstObservedAt,
                            ObservedTitle = entry.ObservedTitle,
                            Provenance = entry.Provenance.ToString()
                        })
                        .ToList()
                })
                .ToList()
        };

        var json = JsonSerializer.Serialize(document, SerializerOptions);
        var tempPath = _repositoryPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _repositoryPath, overwrite: true);
    }

    private static CharacterBadgeAcquisitionSnapshot ToSnapshot(
        CharacterRecordId characterRecordId,
        MutableCharacterState state) =>
        new()
        {
            CharacterRecordId = characterRecordId,
            AccountStableId = state.AccountStableId,
            AcquiredBadgeIds = state.Acquisitions.Keys
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray(),
            Acquisitions = state.Acquisitions.Values
                .OrderBy(entry => entry.CatalogItemId, StringComparer.Ordinal)
                .ToArray()
        };

    private static CharacterBadgeAcquisitionProvenance ParseProvenance(string? value) =>
        Enum.TryParse<CharacterBadgeAcquisitionProvenance>(value, ignoreCase: true, out var parsed)
            ? parsed
            : CharacterBadgeAcquisitionProvenance.LegacyUnknown;

    private sealed class MutableCharacterState
    {
        public required string AccountStableId { get; init; }

        public Dictionary<string, CharacterBadgeAcquisitionEntry> Acquisitions { get; } =
            new(StringComparer.Ordinal);
    }

    private sealed class PersistedRepository
    {
        public int SchemaVersion { get; set; }

        public List<PersistedCharacter> Characters { get; set; } = [];
    }

    private sealed class PersistedCharacter
    {
        public string CharacterRecordId { get; set; } = string.Empty;

        public string AccountStableId { get; set; } = string.Empty;

        public List<PersistedAcquisition> Acquisitions { get; set; } = [];
    }

    private sealed class PersistedAcquisition
    {
        public string CatalogItemId { get; set; } = string.Empty;

        public DateTimeOffset FirstObservedAt { get; set; }

        public string ObservedTitle { get; set; } = string.Empty;

        public string? Provenance { get; set; }
    }
}

internal sealed class NullCharacterBadgeAcquisitionRepository : ICharacterBadgeAcquisitionRepository
{
    public static NullCharacterBadgeAcquisitionRepository Instance { get; } = new();

    public CharacterBadgeAcquisitionSnapshot GetSnapshot(CharacterRecordId characterRecordId) =>
        CharacterBadgeAcquisitionSnapshot.Empty with { CharacterRecordId = characterRecordId };

    public bool IsBadgeAcquired(CharacterRecordId characterRecordId, string catalogItemId) => false;

    public IReadOnlyCollection<string> GetAcquiredBadgeIds(CharacterRecordId characterRecordId) =>
        Array.Empty<string>();

    public CharacterBadgeAcquisitionOperationResult RecordAcquisition(
        CharacterRecordId characterRecordId,
        string accountStableId,
        string catalogItemId,
        string observedTitle,
        DateTimeOffset observedAt) =>
        CharacterBadgeAcquisitionOperationResult.Success();

    public CharacterBadgeAcquisitionOperationResult RecordAcquisition(
        CharacterRecordId characterRecordId,
        string accountStableId,
        string catalogItemId,
        string observedTitle,
        DateTimeOffset observedAt,
        CharacterBadgeAcquisitionProvenance provenance) =>
        CharacterBadgeAcquisitionOperationResult.Success();
}

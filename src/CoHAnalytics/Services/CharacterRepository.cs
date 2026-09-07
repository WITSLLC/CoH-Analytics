using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Persistent account-scoped Character Repository with versioned JSON persistence, atomic
/// saves, corruption isolation, immutable snapshots, and bounded diagnostics (Revision 9
/// §3.6.22.5).
/// </summary>
public sealed class CharacterRepository : ICharacterRepository
{
  public const int PersistenceSchemaVersion = 1;

  private static readonly JsonSerializerOptions SerializerOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true
  };

  private readonly CharacterRepositoryOptions _options;
  private readonly TimeProvider _timeProvider;
  private readonly string _dataDirectory;
  private readonly string _repositoryPath;
  private readonly object _sync = new();
  private readonly Dictionary<CharacterRecordId, MutableRecord> _records = [];
  private readonly Dictionary<CharacterRecordId, CharacterRecordId> _retiredRecordRedirects = [];
  private readonly List<string> _recentOperations = [];

  private CharacterRepositorySnapshot _snapshot = CharacterRepositorySnapshot.Empty;
  private long _revision;
  private int _skippedCorruptRecordCount;
  private int _discardedAliasCount;
  private bool _repositoryFileCorrupted;
  private bool _hasUnsavedChanges;
  private DateTimeOffset? _lastPersistedAt;
  private DateTimeOffset? _lastLoadAt;

  public CharacterRepository(CharacterRepositoryOptions? options = null)
  {
    _options = options ?? new CharacterRepositoryOptions();
    if (_options.MaximumAliasesPerCharacter <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(options), "Maximum aliases per character must be positive.");
    }

    _timeProvider = _options.TimeProvider;
    _dataDirectory = _options.DataDirectory
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoH Analytics");
    _repositoryPath = Path.Combine(_dataDirectory, "characters.json");

    LoadLocked();
  }

  public event EventHandler<CharacterRepositoryChangedEventArgs>? StateChanged;

  public CharacterRepositorySnapshot Current
  {
    get
    {
      lock (_sync)
      {
        return _snapshot;
      }
    }
  }

  public CharacterRepositoryOperationResult EstablishTrustedFromWelcome(
      string accountStableId,
      string displayName,
      DateTimeOffset? observedAt = null)
  {
    return EstablishTrustedLocked(
        accountStableId,
        displayName,
        CharacterTrustState.TrustedFromWelcome,
        "Welcome",
        observedAt);
  }

  public CharacterRepositoryOperationResult EstablishTrustedFromManualConfirmation(
      string accountStableId,
      string displayName)
  {
    return EstablishTrustedLocked(
        accountStableId,
        displayName,
        CharacterTrustState.TrustedFromManualConfirmation,
        "ManualConfirmation");
  }

  public CharacterRepositoryOperationResult RecordInferredObservation(
      CharacterRecordId recordId,
      DateTimeOffset? observedAt = null)
  {
    bool changed;
    CharacterRepositorySnapshot snapshot;
    CharacterRepositoryOperationResult result;
    var activityAt = observedAt ?? _timeProvider.GetUtcNow();

    lock (_sync)
    {
      if (!_records.TryGetValue(recordId, out var record))
      {
        return CharacterRepositoryOperationResult.Failure(
            CharacterRepositoryOutcome.RecordNotFound,
            $"Character record '{recordId}' was not found.");
      }

      changed = TouchLastObservedAtLocked(record, activityAt);
      record.Provenance.InferredObservationCount++;
      record.Provenance.LastInferredObservationAt = activityAt;

      RecordOperationLocked($"Recorded inferred observation for {recordId}.");
      changed = TryPublishLocked(activityAt, out snapshot) || changed;
      result = CharacterRepositoryOperationResult.Success(recordId);

      if (changed)
      {
        if (!TryPersistLocked(activityAt))
        {
          result = CharacterRepositoryOperationResult.Failure(
              CharacterRepositoryOutcome.PersistenceFailed,
              "Inferred observation could not be persisted.");
        }
      }
    }

    if (changed)
    {
      StateChanged?.Invoke(this, new CharacterRepositoryChangedEventArgs(snapshot));
    }

    return result;
  }

  public CharacterRepositoryOperationResult RecordTrustedActivity(
      CharacterRecordId recordId,
      DateTimeOffset observedAt)
  {
    if (!IsValidObservationTimestamp(observedAt))
    {
      return CharacterRepositoryOperationResult.Failure(
          CharacterRepositoryOutcome.InvalidDisplayName,
          "Character activity requires a valid observation timestamp.");
    }

    bool changed;
    CharacterRepositorySnapshot snapshot;
    CharacterRepositoryOperationResult result;

    lock (_sync)
    {
      if (!_records.TryGetValue(recordId, out var record))
      {
        return CharacterRepositoryOperationResult.Failure(
            CharacterRepositoryOutcome.RecordNotFound,
            $"Character record '{recordId}' was not found.");
      }

      changed = TouchLastObservedAtLocked(record, observedAt);
      if (!changed)
      {
        return CharacterRepositoryOperationResult.Success(recordId);
      }

      RecordOperationLocked($"Recorded trusted activity for {recordId}.");
      changed = TryPublishLocked(observedAt, out snapshot);
      result = CharacterRepositoryOperationResult.Success(recordId);

      if (changed)
      {
        if (!TryPersistLocked(observedAt))
        {
          result = CharacterRepositoryOperationResult.Failure(
              CharacterRepositoryOutcome.PersistenceFailed,
              "Trusted character activity could not be persisted.");
        }
      }
    }

    if (changed)
    {
      StateChanged?.Invoke(this, new CharacterRepositoryChangedEventArgs(snapshot));
    }

    return result;
  }

  public CharacterRepositoryOperationResult RecordObservedLevel(
      CharacterRecordId recordId,
      int level,
      DateTimeOffset observedAt)
  {
    bool changed;
    CharacterRepositorySnapshot snapshot;
    CharacterRepositoryOperationResult result;

    lock (_sync)
    {
      if (!_records.TryGetValue(recordId, out var record))
      {
        return CharacterRepositoryOperationResult.Failure(
            CharacterRepositoryOutcome.RecordNotFound,
            $"Character record '{recordId}' was not found.");
      }

      var levelChanged = record.ObservedLevel != level;
      record.ObservedLevel = level;
      record.ObservedLevelObservedAt = observedAt;
      changed = levelChanged;
      changed |= TouchLastObservedAtLocked(record, observedAt);

      RecordOperationLocked($"Recorded observed level {level} for {recordId}.");
      changed = TryPublishLocked(observedAt, out snapshot) || changed;
      result = CharacterRepositoryOperationResult.Success(recordId);

      if (changed)
      {
        if (!TryPersistLocked(observedAt))
        {
          result = CharacterRepositoryOperationResult.Failure(
              CharacterRepositoryOutcome.PersistenceFailed,
              "Observed level could not be persisted.");
        }
      }
    }

    if (changed)
    {
      StateChanged?.Invoke(this, new CharacterRepositoryChangedEventArgs(snapshot));
    }

    return result;
  }

  public CharacterRepositoryOperationResult RecordTrustedObservedDisplayName(
      CharacterRecordId recordId,
      string displayName,
      CharacterTrustState? trustState = null)
  {
    if (!CharacterNameNormalizer.IsValidDisplayName(displayName))
    {
      return CharacterRepositoryOperationResult.Failure(
          CharacterRepositoryOutcome.InvalidDisplayName);
    }

    bool changed;
    CharacterRepositorySnapshot snapshot;
    CharacterRepositoryOperationResult result;
    var normalized = CharacterNameNormalizer.Normalize(displayName);
    var now = _timeProvider.GetUtcNow();

    lock (_sync)
    {
      if (!_records.TryGetValue(recordId, out var record))
      {
        return CharacterRepositoryOperationResult.Failure(
            CharacterRepositoryOutcome.RecordNotFound,
            $"Character record '{recordId}' was not found.");
      }

      changed = ApplyTrustedDisplayNameLocked(
          record,
          displayName,
          normalized,
          now,
          trustState,
          "ObservedDisplayName");
      changed |= ReconcileDuplicateDisplayNameLocked(record, normalized, now);
      changed |= CanonicalizeAliasesLocked(record);
      RecordOperationLocked($"Recorded trusted display name for {recordId}.");
      changed = TryPublishLocked(now, out snapshot) || changed;
      result = CharacterRepositoryOperationResult.Success(recordId);

      if (changed)
      {
        if (!TryPersistLocked(now))
        {
          result = CharacterRepositoryOperationResult.Failure(
              CharacterRepositoryOutcome.PersistenceFailed,
              "Observed display name could not be persisted.");
        }
      }
    }

    if (changed)
    {
      StateChanged?.Invoke(this, new CharacterRepositoryChangedEventArgs(snapshot));
    }

    return result;
  }

  public CharacterRecord? TryFindTrustedByDisplayName(string accountStableId, string displayName)
  {
    if (!IsValidAccountStableId(accountStableId) || !CharacterNameNormalizer.IsValidDisplayName(displayName))
    {
      return null;
    }

    var normalized = CharacterNameNormalizer.Normalize(displayName);

    lock (_sync)
    {
      return TryFindTrustedRecordLocked(accountStableId, normalized)?.ToImmutable();
    }
  }

  public CharacterRecord? TryGetRecord(CharacterRecordId recordId)
  {
    lock (_sync)
    {
      return _records.TryGetValue(recordId, out var record) ? record.ToImmutable() : null;
    }
  }

  public CharacterRecordId ResolveCanonicalRecordId(CharacterRecordId recordId)
  {
    ArgumentNullException.ThrowIfNull(recordId);

    lock (_sync)
    {
      return ResolveCanonicalRecordIdLocked(recordId, out _);
    }
  }

  public IReadOnlyList<CharacterRecordId> GetRecordIdsResolvingTo(CharacterRecordId recordId)
  {
    ArgumentNullException.ThrowIfNull(recordId);

    lock (_sync)
    {
      var canonical = ResolveCanonicalRecordIdLocked(recordId, out _);
      return _retiredRecordRedirects.Keys
          .Append(canonical)
          .Where(candidate => ResolveCanonicalRecordIdLocked(candidate, out _) == canonical)
          .Distinct()
          .OrderBy(candidate => candidate.Value)
          .ToArray();
    }
  }

  public CharacterRecord? TryGetRecordByShortId(string accountStableId, string characterShortId)
  {
    if (!IsValidAccountStableId(accountStableId)
        || !CharacterShortId.TryParse(characterShortId, out var shortId))
    {
      return null;
    }

    lock (_sync)
    {
      foreach (var record in _records.Values)
      {
        if (!string.Equals(record.AccountStableId, accountStableId, StringComparison.Ordinal))
        {
          continue;
        }

        if (string.Equals(record.CharacterShortId, shortId.Value, StringComparison.OrdinalIgnoreCase))
        {
          return record.ToImmutable();
        }
      }

      return null;
    }
  }

  public CharacterRepositoryOperationResult ImportBuildMetadata(
      CharacterRecordId recordId,
      string? primaryPowerSet,
      string? secondaryPowerSet,
      string archetype,
      int? currentBuildNumber,
      DateTimeOffset observedAt)
  {
    if (string.IsNullOrWhiteSpace(archetype))
    {
      return CharacterRepositoryOperationResult.Failure(
          CharacterRepositoryOutcome.InvalidDisplayName,
          "Build metadata requires an archetype.");
    }

    bool changed;
    CharacterRepositorySnapshot snapshot;
    CharacterRepositoryOperationResult result;

    lock (_sync)
    {
      if (!_records.TryGetValue(recordId, out var record))
      {
        return CharacterRepositoryOperationResult.Failure(
            CharacterRepositoryOutcome.RecordNotFound,
            $"Character record '{recordId}' was not found.");
      }

      changed = ApplyBuildMetadataLocked(
          record,
          primaryPowerSet,
          secondaryPowerSet,
          archetype.Trim(),
          currentBuildNumber,
          observedAt);
      RecordOperationLocked($"Imported build metadata for {recordId}.");
      changed = TryPublishLocked(observedAt, out snapshot) || changed;
      result = CharacterRepositoryOperationResult.Success(recordId);

      if (changed)
      {
        if (!TryPersistLocked(observedAt))
        {
          result = CharacterRepositoryOperationResult.Failure(
              CharacterRepositoryOutcome.PersistenceFailed,
              "Build metadata could not be persisted.");
        }
      }
    }

    if (changed)
    {
      StateChanged?.Invoke(this, new CharacterRepositoryChangedEventArgs(snapshot));
    }

    return result;
  }

  public CharacterRepositoryOperationResult SetIconReference(
      CharacterRecordId recordId,
      CharacterIconReference? iconReference)
  {
    if (iconReference is not null && !IsValidIconReference(iconReference))
    {
      return CharacterRepositoryOperationResult.Failure(
          CharacterRepositoryOutcome.InvalidIconReference,
          "Character icon reference is invalid.");
    }

    bool changed;
    CharacterRepositorySnapshot snapshot;
    CharacterRepositoryOperationResult result;
    var now = _timeProvider.GetUtcNow();

    lock (_sync)
    {
      if (!_records.TryGetValue(recordId, out var record))
      {
        return CharacterRepositoryOperationResult.Failure(
            CharacterRepositoryOutcome.RecordNotFound,
            $"Character record '{recordId}' was not found.");
      }

      var normalizedReference = iconReference is null
          ? null
          : iconReference with { IconId = iconReference.IconId.Trim() };
      if (Equals(record.IconReference, normalizedReference))
      {
        return CharacterRepositoryOperationResult.Success(recordId);
      }

      record.IconReference = normalizedReference;
      RecordOperationLocked($"Updated character icon reference for {recordId}.");
      changed = TryPublishLocked(now, out snapshot);
      result = CharacterRepositoryOperationResult.Success(recordId);

      if (changed && !TryPersistLocked(now))
      {
        result = CharacterRepositoryOperationResult.Failure(
            CharacterRepositoryOutcome.PersistenceFailed,
            "Character icon reference could not be persisted.");
      }
    }

    if (changed)
    {
      StateChanged?.Invoke(this, new CharacterRepositoryChangedEventArgs(snapshot));
    }

    return result;
  }

  public CharacterRepositoryDiagnostics GetDiagnostics()
  {
    lock (_sync)
    {
      return new CharacterRepositoryDiagnostics
      {
        LastSnapshotRevision = _revision,
        RecordCount = _records.Count,
        SkippedCorruptRecordCount = _skippedCorruptRecordCount,
        DiscardedAliasCount = _discardedAliasCount,
        RepositoryFileCorrupted = _repositoryFileCorrupted,
        PersistencePath = _repositoryPath,
        PersistenceSchemaVersion = PersistenceSchemaVersion,
        LastPersistedAt = _lastPersistedAt,
        LastLoadAt = _lastLoadAt,
        HasUnsavedChanges = _hasUnsavedChanges,
        Records = [.. _records.Values.Select(record => record.ToImmutable())],
        RecentOperations = [.. _recentOperations]
      };
    }
  }

  private CharacterRepositoryOperationResult EstablishTrustedLocked(
      string accountStableId,
      string displayName,
      CharacterTrustState trustState,
      string operationLabel,
      DateTimeOffset? observedAt = null)
  {
    if (!IsValidAccountStableId(accountStableId))
    {
      return CharacterRepositoryOperationResult.Failure(
          CharacterRepositoryOutcome.InvalidAccountStableId);
    }

    if (!CharacterNameNormalizer.IsValidDisplayName(displayName))
    {
      return CharacterRepositoryOperationResult.Failure(
          CharacterRepositoryOutcome.InvalidDisplayName);
    }

    bool changed;
    CharacterRepositorySnapshot snapshot;
    CharacterRepositoryOperationResult result;
    var normalized = CharacterNameNormalizer.Normalize(displayName);
    var now = observedAt ?? _timeProvider.GetUtcNow();

    lock (_sync)
    {
      var existing = TryFindTrustedRecordLocked(accountStableId, normalized);

      if (existing is not null)
      {
        changed = ApplyTrustedDisplayNameLocked(
            existing,
            displayName,
            normalized,
            now,
            trustState,
            operationLabel);
        changed |= ReconcileDuplicateDisplayNameLocked(existing, normalized, now);
        changed |= CanonicalizeAliasesLocked(existing);
        RecordOperationLocked(
            $"Updated trusted record {existing.RecordId} for account '{accountStableId}' via {operationLabel}.");
        changed = TryPublishLocked(now, out snapshot) || changed;
        result = CharacterRepositoryOperationResult.Success(existing.RecordId);
      }
      else
      {
        var recordId = CharacterRecordId.CreateNew();
        var record = new MutableRecord
        {
          RecordId = recordId,
          AccountStableId = accountStableId,
          CharacterShortId = AssignUniqueShortIdLocked(),
          NormalizedCharacterName = normalized,
          CurrentDisplayName = displayName.Trim(),
          FirstObservedAt = now,
          LastObservedAt = now,
          TrustState = trustState,
          Provenance = new MutableProvenance
          {
            TrustState = trustState,
            EstablishedAt = now,
            InferredObservationCount = 0,
            LastInferredObservationAt = null
          },
          Aliases = [],
          RecordSchemaVersion = CharacterRecord.CurrentSchemaVersion
        };

        _records[recordId] = record;
        RecordOperationLocked(
            $"Created trusted record {recordId} for account '{accountStableId}' via {operationLabel}.");
        changed = TryPublishLocked(now, out snapshot);
        result = CharacterRepositoryOperationResult.Success(recordId);
      }

      if (changed)
      {
        if (!TryPersistLocked(now))
        {
          result = CharacterRepositoryOperationResult.Failure(
              CharacterRepositoryOutcome.PersistenceFailed,
              $"Trusted record mutation for account '{accountStableId}' could not be persisted.");
        }
      }
    }

    if (changed)
    {
      StateChanged?.Invoke(this, new CharacterRepositoryChangedEventArgs(snapshot));
    }

    return result;
  }

  private bool TryPersistLocked(DateTimeOffset now)
  {
    try
    {
      if (_options.SimulatePersistenceFailure)
      {
        throw new IOException("Simulated character repository persistence failure.");
      }

      PersistLocked(now);
      return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      _hasUnsavedChanges = true;
      RecordOperationLocked($"Repository persistence failed ({ex.GetType().Name}).");
      return false;
    }
  }

  private void LoadLocked()
  {
    _records.Clear();
    _retiredRecordRedirects.Clear();
    _skippedCorruptRecordCount = 0;
    _discardedAliasCount = 0;
    _repositoryFileCorrupted = false;
    _lastLoadAt = _timeProvider.GetUtcNow();

    if (!File.Exists(_repositoryPath))
    {
      TryPublishLocked(_lastLoadAt.Value, out _);
      return;
    }

    try
    {
      var json = File.ReadAllText(_repositoryPath);
      var document = JsonSerializer.Deserialize<PersistedRepository>(json, SerializerOptions);

      if (document is null)
      {
        _repositoryFileCorrupted = true;
        RecordOperationLocked("Repository file could not be deserialized; starting empty.");
        TryPublishLocked(_lastLoadAt.Value, out _);
        return;
      }

      if (document.SchemaVersion != PersistenceSchemaVersion)
      {
        _repositoryFileCorrupted = true;
        RecordOperationLocked(
            $"Repository schema version {document.SchemaVersion} is unsupported; starting empty.");
        TryPublishLocked(_lastLoadAt.Value, out _);
        return;
      }

      foreach (var persisted in document.Records)
      {
        if (!TryConvertPersistedRecord(persisted, out var record, out var reason))
        {
          _skippedCorruptRecordCount++;
          RecordOperationLocked($"Skipped corrupt record during load: {reason}.");
          continue;
        }

        _records[record.RecordId] = record;
      }

      foreach (var persistedRedirect in document.RetiredRecordRedirects ?? [])
      {
        if (!TryLoadRetiredRecordRedirectLocked(persistedRedirect))
        {
          _skippedCorruptRecordCount++;
        }
      }

      var canonicalChanged = CanonicalizeAndConsolidateLoadedRecordsLocked();
      var recoveredTimestamps = RecoverObservedTimestampsLocked();
      var backfilledShortIds = BackfillCharacterShortIdsLocked();
      TryPublishLocked(_lastLoadAt.Value, out _);
      if (canonicalChanged || recoveredTimestamps || backfilledShortIds)
      {
        TryPersistLocked(_lastLoadAt.Value);
      }
    }
    catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
    {
      _repositoryFileCorrupted = true;
      RecordOperationLocked($"Repository load failed: {ex.GetType().Name}; starting empty.");
      TryPublishLocked(_lastLoadAt.Value, out _);
    }
  }

  private void PersistLocked(DateTimeOffset now)
  {
    Directory.CreateDirectory(_dataDirectory);

    var document = new PersistedRepository
    {
      SchemaVersion = PersistenceSchemaVersion,
      Records = _records.Values.Select(record => record.ToPersisted()).ToList(),
      RetiredRecordRedirects = _retiredRecordRedirects.Count == 0
          ? null
          : _retiredRecordRedirects
              .OrderBy(pair => pair.Key.Value)
              .Select(pair => new PersistedRecordRedirect
              {
                RetiredRecordId = pair.Key.ToString(),
                SurvivingRecordId = pair.Value.ToString()
              })
              .ToList()
    };

    var json = JsonSerializer.Serialize(document, SerializerOptions);
    var tempPath = _repositoryPath + ".tmp";
    File.WriteAllText(tempPath, json);
    File.Move(tempPath, _repositoryPath, overwrite: true);
    _lastPersistedAt = now;
    _hasUnsavedChanges = false;
  }

  private bool TryPublishLocked(DateTimeOffset now, out CharacterRepositorySnapshot snapshot)
  {
    var candidate = CharacterRepositorySnapshot.Create(
        _records.Values.Select(record => record.ToImmutable()),
        _skippedCorruptRecordCount,
        now,
        _revision + 1);

    if (candidate.IsSemanticallyEquivalentTo(_snapshot))
    {
      snapshot = _snapshot;
      return false;
    }

    _revision++;
    _snapshot = candidate;
    snapshot = candidate;
    return true;
  }

  private void RecordOperationLocked(string message)
  {
    _recentOperations.Add(message);
    if (_recentOperations.Count > _options.MaxRecentDiagnosticsEntries)
    {
      _recentOperations.RemoveAt(0);
    }
  }

  private static bool IsValidAccountStableId(string? accountStableId) =>
      !string.IsNullOrWhiteSpace(accountStableId);

  private static bool IsValidObservationTimestamp(DateTimeOffset timestamp) =>
      timestamp != default;

  private bool TouchLastObservedAtLocked(MutableRecord record, DateTimeOffset observedAt)
  {
    if (!IsValidObservationTimestamp(observedAt) || record.LastObservedAt >= observedAt)
    {
      return false;
    }

    record.LastObservedAt = observedAt;
    return true;
  }

  private bool TouchFirstObservedAtLocked(MutableRecord record, DateTimeOffset observedAt)
  {
    if (!IsValidObservationTimestamp(observedAt))
    {
      return false;
    }

    if (record.FirstObservedAt == default || record.FirstObservedAt > observedAt)
    {
      record.FirstObservedAt = observedAt;
      return true;
    }

    return false;
  }

  private bool RecoverObservedTimestampsLocked()
  {
    var badgeActivity = TryLoadBadgeActivityTimestampsLocked();
    var changed = false;

    foreach (var record in _records.Values)
    {
      changed |= RecoverRecordObservedTimestampsLocked(record, badgeActivity);
    }

    if (changed)
    {
      RecordOperationLocked("Recovered character observed timestamps from persisted evidence.");
    }

    return changed;
  }

  private bool RecoverRecordObservedTimestampsLocked(
      MutableRecord record,
      IReadOnlyDictionary<CharacterRecordId, DateTimeOffset> badgeActivity)
  {
    var candidates = new List<DateTimeOffset>();
    CollectObservationTimestampCandidates(record, badgeActivity, candidates);
    if (candidates.Count == 0)
    {
      return false;
    }

    var latest = candidates.Max();
    var earliest = candidates.Min();
    var changed = TouchLastObservedAtLocked(record, latest);
    changed |= TouchFirstObservedAtLocked(record, earliest);
    return changed;
  }

  private static void CollectObservationTimestampCandidates(
      MutableRecord record,
      IReadOnlyDictionary<CharacterRecordId, DateTimeOffset> badgeActivity,
      List<DateTimeOffset> candidates)
  {
    void Add(DateTimeOffset timestamp)
    {
      if (IsValidObservationTimestamp(timestamp))
      {
        candidates.Add(timestamp);
      }
    }

    void AddOptional(DateTimeOffset? timestamp)
    {
      if (timestamp is DateTimeOffset value)
      {
        Add(value);
      }
    }

    Add(record.FirstObservedAt);
    Add(record.LastObservedAt);
    Add(record.Provenance.EstablishedAt);
    AddOptional(record.Provenance.LastInferredObservationAt);
    AddOptional(record.ObservedLevelObservedAt);

    foreach (var alias in record.Aliases)
    {
      Add(alias.FirstObservedAt);
      Add(alias.LastObservedAt);
    }

    if (badgeActivity.TryGetValue(record.RecordId, out var badgeTimestamp))
    {
      Add(badgeTimestamp);
    }
  }

  private Dictionary<CharacterRecordId, DateTimeOffset> TryLoadBadgeActivityTimestampsLocked()
  {
    var badgePath = ApplicationDataPaths.GetBadgeAcquisitionsPath(_dataDirectory);
    if (!File.Exists(badgePath))
    {
      return [];
    }

    try
    {
      var json = File.ReadAllText(badgePath);
      var document = JsonSerializer.Deserialize<BadgeActivityDocument>(json, SerializerOptions);
      if (document is null || document.SchemaVersion != BadgeActivitySchemaVersion)
      {
        return [];
      }

      var timestamps = new Dictionary<CharacterRecordId, DateTimeOffset>();
      foreach (var persistedCharacter in document.Characters)
      {
        if (!Guid.TryParse(persistedCharacter.CharacterRecordId, out var recordGuid)
            || string.IsNullOrWhiteSpace(persistedCharacter.AccountStableId))
        {
          continue;
        }

        var recordId = CharacterRecordId.FromGuid(recordGuid);
        if (!_records.TryGetValue(recordId, out var record)
            || !string.Equals(
                record.AccountStableId,
                persistedCharacter.AccountStableId,
                StringComparison.Ordinal))
        {
          continue;
        }

        foreach (var acquisition in persistedCharacter.Acquisitions)
        {
          if (!IsValidObservationTimestamp(acquisition.FirstObservedAt))
          {
            continue;
          }

          if (!timestamps.TryGetValue(recordId, out var latest)
              || acquisition.FirstObservedAt > latest)
          {
            timestamps[recordId] = acquisition.FirstObservedAt;
          }
        }
      }

      return timestamps;
    }
    catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
    {
      RecordOperationLocked($"Skipped badge activity recovery: {ex.GetType().Name}.");
      return [];
    }
  }

  private const int BadgeActivitySchemaVersion = 1;

  private sealed class BadgeActivityDocument
  {
    public int SchemaVersion { get; set; }

    public List<BadgeActivityCharacter> Characters { get; set; } = [];
  }

  private sealed class BadgeActivityCharacter
  {
    public string CharacterRecordId { get; set; } = string.Empty;

    public string AccountStableId { get; set; } = string.Empty;

    public List<BadgeActivityAcquisition> Acquisitions { get; set; } = [];
  }

  private sealed class BadgeActivityAcquisition
  {
    public DateTimeOffset FirstObservedAt { get; set; }
  }

  private MutableRecord? TryFindTrustedRecordLocked(string accountStableId, string normalized)
  {
    foreach (var record in _records.Values)
    {
      if (!string.Equals(record.AccountStableId, accountStableId, StringComparison.Ordinal))
      {
        continue;
      }

      if (CharacterNameNormalizer.NamesMatch(record.NormalizedCharacterName, normalized))
      {
        return record;
      }

      if (record.Aliases.Any(alias =>
              CharacterNameNormalizer.NamesMatch(
                  CharacterNameNormalizer.Normalize(alias.DisplayName),
                  normalized)))
      {
        return record;
      }
    }

    return null;
  }

  private bool ApplyTrustedDisplayNameLocked(
      MutableRecord record,
      string displayName,
      string normalized,
      DateTimeOffset observedAt,
      CharacterTrustState? trustState,
      string operationLabel)
  {
    var trimmedDisplay = displayName.Trim().Normalize(NormalizationForm.FormC);
    var changed = false;

    if (!CharacterNameNormalizer.NamesMatch(record.NormalizedCharacterName, normalized))
    {
      AddAliasForDisplayNameLocked(record, record.CurrentDisplayName, observedAt);
      record.NormalizedCharacterName = normalized;
      record.CurrentDisplayName = trimmedDisplay;
      changed = true;
    }
    else if (!string.Equals(record.CurrentDisplayName, trimmedDisplay, StringComparison.Ordinal))
    {
      record.CurrentDisplayName = trimmedDisplay;
      changed = true;
    }

    if (record.LastObservedAt < observedAt)
    {
      record.LastObservedAt = observedAt;
      changed = true;
    }

    if (trustState is CharacterTrustState resolvedTrustState)
    {
      if (resolvedTrustState == CharacterTrustState.TrustedFromManualConfirmation
          || record.TrustState == CharacterTrustState.TrustedFromManualConfirmation)
      {
        record.TrustState = CharacterTrustState.TrustedFromManualConfirmation;
        record.Provenance.TrustState = CharacterTrustState.TrustedFromManualConfirmation;
      }
      else
      {
        record.TrustState = resolvedTrustState;
        record.Provenance.TrustState = resolvedTrustState;
      }

      record.Provenance.EstablishedAt = observedAt;
      changed = true;
    }

    if (changed)
    {
      RecordOperationLocked(
          $"Applied trusted display name '{trimmedDisplay}' to {record.RecordId} via {operationLabel}.");
    }

    return changed;
  }

  private bool ReconcileDuplicateDisplayNameLocked(
      MutableRecord survivor,
      string normalizedDisplayName,
      DateTimeOffset observedAt)
  {
    var duplicate = _records.Values.FirstOrDefault(record =>
        record.RecordId != survivor.RecordId
        && string.Equals(record.AccountStableId, survivor.AccountStableId, StringComparison.Ordinal)
        && CharacterNameNormalizer.NamesMatch(record.NormalizedCharacterName, normalizedDisplayName));

    if (duplicate is null)
    {
      return false;
    }

    MergeDuplicateIntoSurvivorLocked(survivor, duplicate);
    _records.Remove(duplicate.RecordId);
    RecordOperationLocked(
        $"Reconciled duplicate character record {duplicate.RecordId} into {survivor.RecordId} after rename.");
    return true;
  }

  private void AddAliasForDisplayNameLocked(
      MutableRecord record,
      string displayName,
      DateTimeOffset observedAt)
  {
    if (!CharacterNameNormalizer.IsValidDisplayName(displayName))
    {
      return;
    }

    var trimmedDisplay = displayName.Trim().Normalize(NormalizationForm.FormC);
    var normalized = CharacterNameNormalizer.Normalize(trimmedDisplay);

    if (record.Aliases.Any(alias =>
            CharacterNameNormalizer.NamesMatch(
                CharacterNameNormalizer.Normalize(alias.DisplayName),
                normalized)))
    {
      return;
    }

    record.Aliases.Add(new MutableAlias
    {
      DisplayName = trimmedDisplay,
      FirstObservedAt = observedAt,
      LastObservedAt = observedAt
    });
  }

  private bool TryConvertPersistedRecord(
      PersistedRecord persisted,
      out MutableRecord record,
      out string reason)
  {
    if (!Guid.TryParse(persisted.RecordId, out var recordGuid))
    {
      record = null!;
      reason = "invalid record id";
      return false;
    }

    if (!IsValidAccountStableId(persisted.AccountStableId)
        || !CharacterNameNormalizer.IsValidDisplayName(persisted.CurrentDisplayName))
    {
      record = null!;
      reason = "missing account or display name";
      return false;
    }

    if (!Enum.IsDefined(persisted.TrustState))
    {
      record = null!;
      reason = "invalid trust state";
      return false;
    }

    var canonicalDisplayName = persisted.CurrentDisplayName.Trim().Normalize(NormalizationForm.FormC);
    var normalized = CharacterNameNormalizer.Normalize(canonicalDisplayName);
    var persistedAliases = persisted.Aliases ?? [];
    var aliases = persistedAliases
        .Where(alias => CharacterNameNormalizer.IsValidDisplayName(alias.DisplayName))
        .Select(alias => new MutableAlias
        {
          DisplayName = alias.DisplayName.Trim().Normalize(NormalizationForm.FormC),
          FirstObservedAt = alias.FirstObservedAt,
          LastObservedAt = alias.LastObservedAt
        })
        .ToList();
    var invalidAliasCount = persistedAliases.Count - aliases.Count;
    if (invalidAliasCount > 0)
    {
      AddDiscardedAliasCountLocked(invalidAliasCount);
      RecordOperationLocked(
          $"Discarded {invalidAliasCount} invalid character alias(es) during repository load.");
    }

    var iconReference = TryConvertPersistedIconReference(persisted.IconReference);
    var requiresCanonicalRewrite = !string.Equals(
            persisted.CurrentDisplayName,
            canonicalDisplayName,
            StringComparison.Ordinal)
        || !string.Equals(persisted.NormalizedCharacterName, normalized, StringComparison.Ordinal)
        || invalidAliasCount > 0
        || persistedAliases.Zip(aliases).Any(pair =>
            !string.Equals(pair.First.DisplayName, pair.Second.DisplayName, StringComparison.Ordinal))
        || (persisted.IconReference is not null && iconReference is null);

    record = new MutableRecord
    {
      RecordId = CharacterRecordId.FromGuid(recordGuid),
      AccountStableId = persisted.AccountStableId,
      CharacterShortId = NormalizePersistedShortId(persisted.CharacterShortId),
      NormalizedCharacterName = normalized,
      CurrentDisplayName = canonicalDisplayName,
      FirstObservedAt = persisted.FirstObservedAt,
      LastObservedAt = persisted.LastObservedAt,
      TrustState = persisted.TrustState,
      Provenance = new MutableProvenance
      {
        TrustState = persisted.Provenance?.TrustState ?? persisted.TrustState,
        EstablishedAt = persisted.Provenance?.EstablishedAt ?? persisted.FirstObservedAt,
        InferredObservationCount = persisted.Provenance?.InferredObservationCount ?? 0,
        LastInferredObservationAt = persisted.Provenance?.LastInferredObservationAt
      },
      ObservedLevel = persisted.ObservedLevel,
      ObservedLevelObservedAt = persisted.ObservedLevelObservedAt,
      PrimaryPowerSet = persisted.PrimaryPowerSet,
      SecondaryPowerSet = persisted.SecondaryPowerSet,
      Archetype = persisted.Archetype,
      CurrentBuildNumber = persisted.CurrentBuildNumber,
      BuildMetadataObservedAt = persisted.BuildMetadataObservedAt,
      IconReference = iconReference,
      Aliases = aliases,
      RecordSchemaVersion = persisted.RecordSchemaVersion,
      RequiresCanonicalRewrite = requiresCanonicalRewrite
    };

    reason = string.Empty;
    return true;
  }

  private bool CanonicalizeAndConsolidateLoadedRecordsLocked()
  {
    var changed = false;

    foreach (var record in _records.Values)
    {
      changed |= record.RequiresCanonicalRewrite;
      record.RequiresCanonicalRewrite = false;
      var trimmedDisplay = record.CurrentDisplayName.Trim().Normalize(NormalizationForm.FormC);
      var canonicalNormalized = CharacterNameNormalizer.Normalize(trimmedDisplay);
      if (!string.Equals(record.CurrentDisplayName, trimmedDisplay, StringComparison.Ordinal)
          || !string.Equals(record.NormalizedCharacterName, canonicalNormalized, StringComparison.Ordinal))
      {
        changed = true;
      }

      record.CurrentDisplayName = trimmedDisplay;
      record.NormalizedCharacterName = canonicalNormalized;
      changed |= CanonicalizeAliasesLocked(record);
    }

    var duplicateGroups = _records.Values
        .GroupBy(record => record.AccountStableId, StringComparer.Ordinal)
        .SelectMany(accountGroup => accountGroup
            .GroupBy(record => record.NormalizedCharacterName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1));

    foreach (var group in duplicateGroups)
    {
      var ordered = group
          .OrderBy(record => record.FirstObservedAt)
          .ThenBy(record => record.RecordId.Value)
          .ToList();
      var survivor = ordered[0];
      foreach (var duplicate in ordered.Skip(1))
      {
        MergeDuplicateIntoSurvivorLocked(survivor, duplicate);
        _records.Remove(duplicate.RecordId);
        changed = true;
        RecordOperationLocked($"Consolidated duplicate character record {duplicate.RecordId}.");
      }
    }

    return changed;
  }

  private void MergeDuplicateIntoSurvivorLocked(MutableRecord survivor, MutableRecord duplicate)
  {
    AddRetiredRecordRedirectLocked(duplicate.RecordId, survivor.RecordId);

    var priorSurvivorDisplay = survivor.CurrentDisplayName.Trim().Normalize(NormalizationForm.FormC);
    var displaySource = survivor.LastObservedAt > duplicate.LastObservedAt
        || (survivor.LastObservedAt == duplicate.LastObservedAt
            && survivor.RecordId.Value.CompareTo(duplicate.RecordId.Value) <= 0)
        ? survivor
        : duplicate;

    survivor.FirstObservedAt = survivor.FirstObservedAt <= duplicate.FirstObservedAt
        ? survivor.FirstObservedAt
        : duplicate.FirstObservedAt;
    survivor.LastObservedAt = survivor.LastObservedAt >= duplicate.LastObservedAt
        ? survivor.LastObservedAt
        : duplicate.LastObservedAt;

    survivor.CurrentDisplayName = displaySource.CurrentDisplayName.Trim().Normalize(NormalizationForm.FormC);
    survivor.NormalizedCharacterName = CharacterNameNormalizer.Normalize(survivor.CurrentDisplayName);

    if (duplicate.TrustState == CharacterTrustState.TrustedFromManualConfirmation
        || survivor.TrustState == CharacterTrustState.TrustedFromManualConfirmation)
    {
      survivor.TrustState = CharacterTrustState.TrustedFromManualConfirmation;
      survivor.Provenance.TrustState = CharacterTrustState.TrustedFromManualConfirmation;
    }
    else
    {
      survivor.TrustState = CharacterTrustState.TrustedFromWelcome;
      survivor.Provenance.TrustState = CharacterTrustState.TrustedFromWelcome;
    }

    survivor.Provenance.EstablishedAt = survivor.Provenance.EstablishedAt <= duplicate.Provenance.EstablishedAt
        ? survivor.Provenance.EstablishedAt
        : duplicate.Provenance.EstablishedAt;

    var combinedInferredCount = survivor.Provenance.InferredObservationCount
        + duplicate.Provenance.InferredObservationCount;
    survivor.Provenance.InferredObservationCount = combinedInferredCount < 0
        ? int.MaxValue
        : combinedInferredCount;

    var survivorLastInferred = survivor.Provenance.LastInferredObservationAt;
    var duplicateLastInferred = duplicate.Provenance.LastInferredObservationAt;
    survivor.Provenance.LastInferredObservationAt = survivorLastInferred is null
        ? duplicateLastInferred
        : duplicateLastInferred is null
            ? survivorLastInferred
            : survivorLastInferred >= duplicateLastInferred
                ? survivorLastInferred
                : duplicateLastInferred;

    if (survivor.ObservedLevelObservedAt is null)
    {
      survivor.ObservedLevel = duplicate.ObservedLevel;
      survivor.ObservedLevelObservedAt = duplicate.ObservedLevelObservedAt;
    }
    else if (duplicate.ObservedLevelObservedAt is not null
        && duplicate.ObservedLevelObservedAt > survivor.ObservedLevelObservedAt)
    {
      survivor.ObservedLevel = duplicate.ObservedLevel;
      survivor.ObservedLevelObservedAt = duplicate.ObservedLevelObservedAt;
    }

    if (string.IsNullOrWhiteSpace(survivor.CharacterShortId)
        && !string.IsNullOrWhiteSpace(duplicate.CharacterShortId))
    {
      survivor.CharacterShortId = duplicate.CharacterShortId;
    }

    MergeBuildMetadataLocked(survivor, duplicate);

    if (survivor.IconReference is null && duplicate.IconReference is not null)
    {
      survivor.IconReference = duplicate.IconReference;
    }

    AddAliasForDisplayNameLocked(survivor, priorSurvivorDisplay, survivor.LastObservedAt);
    AddAliasForDisplayNameLocked(survivor, duplicate.CurrentDisplayName, duplicate.LastObservedAt);
    survivor.Aliases.AddRange(duplicate.Aliases);
    CanonicalizeAliasesLocked(survivor);
  }

  private void AddRetiredRecordRedirectLocked(
      CharacterRecordId retiredRecordId,
      CharacterRecordId survivingRecordId)
  {
    if (retiredRecordId == survivingRecordId)
    {
      throw new InvalidOperationException("A character record cannot redirect to itself.");
    }

    if (_retiredRecordRedirects.TryGetValue(retiredRecordId, out var existing))
    {
      if (existing != survivingRecordId)
      {
        throw new InvalidOperationException(
            $"Character record {retiredRecordId} already redirects to {existing}.");
      }

      return;
    }

    var current = survivingRecordId;
    var visited = new HashSet<CharacterRecordId>();
    while (_retiredRecordRedirects.TryGetValue(current, out var next))
    {
      if (current == retiredRecordId || !visited.Add(current))
      {
        throw new InvalidOperationException("Character record redirect cycle detected.");
      }

      current = next;
    }

    if (current == retiredRecordId)
    {
      throw new InvalidOperationException("Character record redirect cycle detected.");
    }

    _retiredRecordRedirects.Add(retiredRecordId, survivingRecordId);
    RecordOperationLocked(
        $"Redirected retired character record {retiredRecordId} to {survivingRecordId}.");
  }

  private bool TryLoadRetiredRecordRedirectLocked(PersistedRecordRedirect persisted)
  {
    if (!Guid.TryParse(persisted.RetiredRecordId, out var retiredGuid)
        || retiredGuid == Guid.Empty
        || !Guid.TryParse(persisted.SurvivingRecordId, out var survivingGuid)
        || survivingGuid == Guid.Empty)
    {
      RecordOperationLocked("Skipped corrupt character record redirect with an invalid identity.");
      return false;
    }

    var retired = CharacterRecordId.FromGuid(retiredGuid);
    var surviving = CharacterRecordId.FromGuid(survivingGuid);
    if (_records.ContainsKey(retired))
    {
      RecordOperationLocked(
          $"Skipped character record redirect for active record {retired}.");
      return false;
    }

    try
    {
      AddRetiredRecordRedirectLocked(retired, surviving);
      return true;
    }
    catch (InvalidOperationException exception)
    {
      RecordOperationLocked(
          $"Skipped corrupt character record redirect {retired} -> {surviving}: {exception.Message}");
      return false;
    }
  }

  private CharacterRecordId ResolveCanonicalRecordIdLocked(
      CharacterRecordId recordId,
      out bool cycleDetected)
  {
    var original = recordId;
    var current = recordId;
    var visited = new HashSet<CharacterRecordId>();
    while (_retiredRecordRedirects.TryGetValue(current, out var next))
    {
      if (!visited.Add(current))
      {
        cycleDetected = true;
        return original;
      }

      current = next;
    }

    cycleDetected = false;
    return current;
  }

  private bool CanonicalizeAliasesLocked(MutableRecord record)
  {
    if (record.Aliases.Count == 0)
    {
      return false;
    }

    var original = record.Aliases.ToArray();
    var canonical = new List<MutableAlias>();
    foreach (var alias in original)
    {
      var displayName = alias.DisplayName.Trim().Normalize(NormalizationForm.FormC);
      var normalizedName = CharacterNameNormalizer.Normalize(displayName);
      if (CharacterNameNormalizer.NamesMatch(normalizedName, record.NormalizedCharacterName))
      {
        continue;
      }

      var existingIndex = canonical.FindIndex(existing =>
          CharacterNameNormalizer.NamesMatch(
              CharacterNameNormalizer.Normalize(existing.DisplayName),
              normalizedName));

      if (existingIndex < 0)
      {
        canonical.Add(new MutableAlias
        {
          DisplayName = displayName,
          FirstObservedAt = alias.FirstObservedAt,
          LastObservedAt = alias.LastObservedAt
        });
        continue;
      }

      var existing = canonical[existingIndex];
      var useAliasDisplay = alias.LastObservedAt > existing.LastObservedAt
          || (alias.LastObservedAt == existing.LastObservedAt
              && string.CompareOrdinal(displayName, existing.DisplayName) < 0);
      canonical[existingIndex] = new MutableAlias
      {
        DisplayName = useAliasDisplay ? displayName : existing.DisplayName,
        FirstObservedAt = existing.FirstObservedAt <= alias.FirstObservedAt
            ? existing.FirstObservedAt
            : alias.FirstObservedAt,
        LastObservedAt = existing.LastObservedAt >= alias.LastObservedAt
            ? existing.LastObservedAt
            : alias.LastObservedAt
      };
    }

    canonical = canonical
        .OrderBy(alias => alias.FirstObservedAt)
        .ThenBy(alias => CharacterNameNormalizer.Normalize(alias.DisplayName), StringComparer.OrdinalIgnoreCase)
        .ThenBy(alias => alias.DisplayName, StringComparer.Ordinal)
        .ThenBy(alias => alias.LastObservedAt)
        .Take(_options.MaximumAliasesPerCharacter)
        .ToList();

    var discarded = original.Length - canonical.Count;
    if (discarded > 0)
    {
      AddDiscardedAliasCountLocked(discarded);
      RecordOperationLocked(
          $"Discarded {discarded} duplicate or excess character alias(es) during canonicalization.");
    }

    var changed = original.Length != canonical.Count
        || !original.Zip(canonical).All(pair =>
            string.Equals(pair.First.DisplayName, pair.Second.DisplayName, StringComparison.Ordinal)
            && pair.First.FirstObservedAt == pair.Second.FirstObservedAt
            && pair.First.LastObservedAt == pair.Second.LastObservedAt);

    record.Aliases.Clear();
    record.Aliases.AddRange(canonical);
    return changed;
  }

  private void AddDiscardedAliasCountLocked(int count)
  {
    _discardedAliasCount = count > int.MaxValue - _discardedAliasCount
        ? int.MaxValue
        : _discardedAliasCount + count;
  }

  private sealed class MutableRecord
  {
    public required CharacterRecordId RecordId { get; init; }

    public required string AccountStableId { get; init; }

    public string? CharacterShortId { get; set; }

    public required string NormalizedCharacterName { get; set; }

    public required string CurrentDisplayName { get; set; }

    public required DateTimeOffset FirstObservedAt { get; set; }

    public DateTimeOffset LastObservedAt { get; set; }

    public CharacterTrustState TrustState { get; set; }

    public required MutableProvenance Provenance { get; set; }

    public List<MutableAlias> Aliases { get; init; } = [];

    public bool RequiresCanonicalRewrite { get; set; }

    public int RecordSchemaVersion { get; init; }

    public int? ObservedLevel { get; set; }

    public DateTimeOffset? ObservedLevelObservedAt { get; set; }

    public string? PrimaryPowerSet { get; set; }

    public string? SecondaryPowerSet { get; set; }

    public string? Archetype { get; set; }

    public int? CurrentBuildNumber { get; set; }

    public DateTimeOffset? BuildMetadataObservedAt { get; set; }

    public CharacterIconReference? IconReference { get; set; }

    public CharacterRecord ToImmutable() => new()
    {
      RecordId = RecordId,
      AccountStableId = AccountStableId,
      CharacterShortId = CharacterShortId,
      NormalizedCharacterName = NormalizedCharacterName,
      CurrentDisplayName = CurrentDisplayName,
      FirstObservedAt = FirstObservedAt,
      LastObservedAt = LastObservedAt,
      TrustState = TrustState,
      Provenance = new CharacterProvenanceSummary
      {
        TrustState = Provenance.TrustState,
        EstablishedAt = Provenance.EstablishedAt,
        InferredObservationCount = Provenance.InferredObservationCount,
        LastInferredObservationAt = Provenance.LastInferredObservationAt
      },
      ObservedLevel = ObservedLevel,
      ObservedLevelObservedAt = ObservedLevelObservedAt,
      PrimaryPowerSet = PrimaryPowerSet,
      SecondaryPowerSet = SecondaryPowerSet,
      Archetype = Archetype,
      CurrentBuildNumber = CurrentBuildNumber,
      BuildMetadataObservedAt = BuildMetadataObservedAt,
      IconReference = IconReference,
      Aliases = Aliases
          .Select(alias => new CharacterAlias
          {
            DisplayName = alias.DisplayName,
            FirstObservedAt = alias.FirstObservedAt,
            LastObservedAt = alias.LastObservedAt
          })
          .ToList(),
      RecordSchemaVersion = RecordSchemaVersion
    };

    public PersistedRecord ToPersisted() => new()
    {
      RecordId = RecordId.Value.ToString("n"),
      AccountStableId = AccountStableId,
      CharacterShortId = CharacterShortId,
      NormalizedCharacterName = NormalizedCharacterName,
      CurrentDisplayName = CurrentDisplayName,
      FirstObservedAt = FirstObservedAt,
      LastObservedAt = LastObservedAt,
      TrustState = TrustState,
      Provenance = new PersistedProvenance
      {
        TrustState = Provenance.TrustState,
        EstablishedAt = Provenance.EstablishedAt,
        InferredObservationCount = Provenance.InferredObservationCount,
        LastInferredObservationAt = Provenance.LastInferredObservationAt
      },
      ObservedLevel = ObservedLevel,
      ObservedLevelObservedAt = ObservedLevelObservedAt,
      PrimaryPowerSet = PrimaryPowerSet,
      SecondaryPowerSet = SecondaryPowerSet,
      Archetype = Archetype,
      CurrentBuildNumber = CurrentBuildNumber,
      BuildMetadataObservedAt = BuildMetadataObservedAt,
      IconReference = IconReference is null
          ? null
          : new PersistedIconReference
          {
            Kind = IconReference.Kind.ToString(),
            IconId = IconReference.IconId
          },
      Aliases = Aliases
          .Select(alias => new PersistedAlias
          {
            DisplayName = alias.DisplayName,
            FirstObservedAt = alias.FirstObservedAt,
            LastObservedAt = alias.LastObservedAt
          })
          .ToList(),
      RecordSchemaVersion = RecordSchemaVersion
    };
  }

  private bool BackfillCharacterShortIdsLocked()
  {
    var changed = false;
    foreach (var record in _records.Values)
    {
      if (!string.IsNullOrWhiteSpace(record.CharacterShortId))
      {
        record.CharacterShortId = NormalizePersistedShortId(record.CharacterShortId);
        continue;
      }

      record.CharacterShortId = AssignUniqueShortIdLocked();
      changed = true;
      RecordOperationLocked($"Backfilled character short id {record.CharacterShortId} for {record.RecordId}.");
    }

    if (changed)
    {
      RecordOperationLocked("Backfilled missing character short ids during repository load.");
    }

    return changed;
  }

  private string AssignUniqueShortIdLocked()
  {
    for (var attempt = 0; attempt < 64; attempt++)
    {
      var candidate = CharacterShortId.CreateNew().Value;
      if (!IsShortIdInUseLocked(candidate))
      {
        return candidate;
      }
    }

    throw new InvalidOperationException("Unable to allocate a unique character short id.");
  }

  private bool IsShortIdInUseLocked(string shortId)
  {
    foreach (var record in _records.Values)
    {
      if (string.Equals(record.CharacterShortId, shortId, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }

  private static string? NormalizePersistedShortId(string? shortId) =>
      CharacterShortId.TryParse(shortId, out var parsed) ? parsed.Value : null;

  private static bool IsValidIconReference(CharacterIconReference iconReference) =>
      Enum.IsDefined(iconReference.Kind)
      && !string.IsNullOrWhiteSpace(iconReference.IconId)
      && iconReference.IconId.Trim().Length <= 128;

  private static CharacterIconReference? TryConvertPersistedIconReference(
      PersistedIconReference? persisted)
  {
    if (persisted is null
        || !Enum.TryParse<CharacterIconKind>(persisted.Kind, ignoreCase: false, out var kind))
    {
      return null;
    }

    var candidate = new CharacterIconReference
    {
      Kind = kind,
      IconId = persisted.IconId
    };
    return IsValidIconReference(candidate)
        ? candidate with { IconId = candidate.IconId.Trim() }
        : null;
  }

  private bool ApplyBuildMetadataLocked(
      MutableRecord record,
      string? primaryPowerSet,
      string? secondaryPowerSet,
      string archetype,
      int? currentBuildNumber,
      DateTimeOffset observedAt)
  {
    var changed = false;

    if (!string.Equals(record.Archetype, archetype, StringComparison.Ordinal))
    {
      record.Archetype = archetype;
      changed = true;
    }

    if (!ShouldApplyPowersetMetadata(record.PrimaryPowerSet, primaryPowerSet))
    {
      primaryPowerSet = record.PrimaryPowerSet;
    }
    else if (!string.Equals(record.PrimaryPowerSet, primaryPowerSet, StringComparison.Ordinal))
    {
      record.PrimaryPowerSet = primaryPowerSet;
      changed = true;
    }

    if (!ShouldApplyPowersetMetadata(record.SecondaryPowerSet, secondaryPowerSet))
    {
      secondaryPowerSet = record.SecondaryPowerSet;
    }
    else if (!string.Equals(record.SecondaryPowerSet, secondaryPowerSet, StringComparison.Ordinal))
    {
      record.SecondaryPowerSet = secondaryPowerSet;
      changed = true;
    }

    if (record.CurrentBuildNumber != currentBuildNumber)
    {
      record.CurrentBuildNumber = currentBuildNumber;
      changed = true;
    }

    if (record.BuildMetadataObservedAt != observedAt)
    {
      record.BuildMetadataObservedAt = observedAt;
      changed = true;
    }

    return changed;
  }

  private static bool ShouldApplyPowersetMetadata(string? existingValue, string? incomingValue)
  {
    if (string.IsNullOrWhiteSpace(incomingValue))
    {
      return false;
    }

    if (string.IsNullOrWhiteSpace(existingValue))
    {
      return true;
    }

    if (HomecomingPowersetDisplayNames.IsGenericCategoryDisplayName(incomingValue)
        && !HomecomingPowersetDisplayNames.IsGenericCategoryDisplayName(existingValue))
    {
      return false;
    }

    return true;
  }

  private static void MergeBuildMetadataLocked(MutableRecord survivor, MutableRecord duplicate)
  {
    if (string.IsNullOrWhiteSpace(survivor.Archetype) && !string.IsNullOrWhiteSpace(duplicate.Archetype))
    {
      survivor.Archetype = duplicate.Archetype;
    }

    if (string.IsNullOrWhiteSpace(survivor.PrimaryPowerSet) && !string.IsNullOrWhiteSpace(duplicate.PrimaryPowerSet))
    {
      survivor.PrimaryPowerSet = duplicate.PrimaryPowerSet;
    }

    if (string.IsNullOrWhiteSpace(survivor.SecondaryPowerSet) && !string.IsNullOrWhiteSpace(duplicate.SecondaryPowerSet))
    {
      survivor.SecondaryPowerSet = duplicate.SecondaryPowerSet;
    }

    if (survivor.CurrentBuildNumber is null && duplicate.CurrentBuildNumber is not null)
    {
      survivor.CurrentBuildNumber = duplicate.CurrentBuildNumber;
    }

    if (survivor.BuildMetadataObservedAt is null && duplicate.BuildMetadataObservedAt is not null)
    {
      survivor.BuildMetadataObservedAt = duplicate.BuildMetadataObservedAt;
    }
    else if (duplicate.BuildMetadataObservedAt is not null
        && survivor.BuildMetadataObservedAt < duplicate.BuildMetadataObservedAt)
    {
      survivor.Archetype = duplicate.Archetype ?? survivor.Archetype;
      survivor.PrimaryPowerSet = duplicate.PrimaryPowerSet ?? survivor.PrimaryPowerSet;
      survivor.SecondaryPowerSet = duplicate.SecondaryPowerSet ?? survivor.SecondaryPowerSet;
      survivor.CurrentBuildNumber = duplicate.CurrentBuildNumber ?? survivor.CurrentBuildNumber;
      survivor.BuildMetadataObservedAt = duplicate.BuildMetadataObservedAt;
    }
  }

  private sealed class MutableProvenance
  {
    public CharacterTrustState TrustState { get; set; }

    public DateTimeOffset EstablishedAt { get; set; }

    public int InferredObservationCount { get; set; }

    public DateTimeOffset? LastInferredObservationAt { get; set; }
  }

  private sealed class MutableAlias
  {
    public required string DisplayName { get; init; }

    public required DateTimeOffset FirstObservedAt { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }
  }

  private sealed class PersistedRepository
  {
    public int SchemaVersion { get; set; }

    public List<PersistedRecord> Records { get; set; } = [];

    public List<PersistedRecordRedirect>? RetiredRecordRedirects { get; set; }
  }

  private sealed class PersistedRecordRedirect
  {
    public string RetiredRecordId { get; set; } = string.Empty;

    public string SurvivingRecordId { get; set; } = string.Empty;
  }

  private sealed class PersistedRecord
  {
    public string RecordId { get; set; } = string.Empty;

    public string AccountStableId { get; set; } = string.Empty;

    public string? CharacterShortId { get; set; }

    public string NormalizedCharacterName { get; set; } = string.Empty;

    public string CurrentDisplayName { get; set; } = string.Empty;

    public DateTimeOffset FirstObservedAt { get; set; }

    public DateTimeOffset LastObservedAt { get; set; }

    public CharacterTrustState TrustState { get; set; }

    public PersistedProvenance? Provenance { get; set; }

    public List<PersistedAlias>? Aliases { get; set; }

    public int RecordSchemaVersion { get; set; } = CharacterRecord.CurrentSchemaVersion;

    public int? ObservedLevel { get; set; }

    public DateTimeOffset? ObservedLevelObservedAt { get; set; }

    public string? PrimaryPowerSet { get; set; }

    public string? SecondaryPowerSet { get; set; }

    public string? Archetype { get; set; }

    public int? CurrentBuildNumber { get; set; }

    public DateTimeOffset? BuildMetadataObservedAt { get; set; }

    public PersistedIconReference? IconReference { get; set; }
  }

  private sealed class PersistedIconReference
  {
    public string Kind { get; set; } = string.Empty;

    public string IconId { get; set; } = string.Empty;
  }

  private sealed class PersistedProvenance
  {
    public CharacterTrustState TrustState { get; set; }

    public DateTimeOffset EstablishedAt { get; set; }

    public int InferredObservationCount { get; set; }

    public DateTimeOffset? LastInferredObservationAt { get; set; }
  }

  private sealed class PersistedAlias
  {
    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset FirstObservedAt { get; set; }

    public DateTimeOffset LastObservedAt { get; set; }
  }
}

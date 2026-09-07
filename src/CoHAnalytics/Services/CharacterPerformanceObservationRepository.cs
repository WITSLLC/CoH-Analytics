using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Persists one versioned JSON document per immutable character-performance observation.
/// </summary>
public sealed class CharacterPerformanceObservationRepository : ICharacterPerformanceObservationRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly Dictionary<ObservationKey, CharacterPerformanceObservation> _observations = [];
    private readonly List<string> _malformedFileReports = [];
    private readonly Action<string, string> _replaceFile;
    private readonly Action<string> _deleteFile;

    public CharacterPerformanceObservationRepository(string? dataDirectory = null)
        : this(dataDirectory, ReplaceFile, File.Delete)
    {
    }

    internal CharacterPerformanceObservationRepository(
        string? dataDirectory,
        Action<string, string> replaceFile)
        : this(dataDirectory, replaceFile, File.Delete)
    {
    }

    internal CharacterPerformanceObservationRepository(
        string? dataDirectory,
        Action<string, string> replaceFile,
        Action<string> deleteFile)
    {
        _replaceFile = replaceFile ?? throw new ArgumentNullException(nameof(replaceFile));
        _deleteFile = deleteFile ?? throw new ArgumentNullException(nameof(deleteFile));
        ObservationDirectory =
            ApplicationDataPaths.GetCharacterPerformanceObservationsDirectory(dataDirectory);
        LoadFromDisk();
    }

    public string ObservationDirectory { get; }

    public event EventHandler? Changed;

    public IReadOnlyList<string> MalformedFileReports
    {
        get
        {
            lock (_sync)
            {
                return _malformedFileReports.ToArray();
            }
        }
    }

    public CharacterPerformanceObservationWriteResult Persist(
        CharacterPerformanceObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!TryValidate(observation, out var validationError))
        {
            return Result(
                CharacterPerformanceObservationWriteOutcome.InvalidObservation,
                detail: validationError);
        }

        var normalized = Normalize(observation);
        var key = ObservationKey.From(normalized);

        lock (_sync)
        {
            if (_observations.TryGetValue(key, out var existing))
            {
                return ResolveDuplicate(existing, normalized, BuildFilePath(key));
            }

            Directory.CreateDirectory(ObservationDirectory);
            var finalPath = BuildFilePath(key);
            if (File.Exists(finalPath))
            {
                return ResolveExistingFile(finalPath, normalized);
            }

            var tempPath = finalPath + "." + Guid.NewGuid().ToString("n") + ".tmp";
            try
            {
                WriteAtomically(tempPath, finalPath, ToDocument(normalized));
                _observations.Add(key, normalized);
                PublishChanged();
                return Result(
                    CharacterPerformanceObservationWriteOutcome.Persisted,
                    finalPath);
            }
            catch (IOException) when (File.Exists(finalPath))
            {
                return ResolveExistingFile(finalPath, normalized);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return Result(
                    CharacterPerformanceObservationWriteOutcome.PersistenceFailed,
                    finalPath,
                    exception.Message);
            }
            finally
            {
                TryDeleteTemporaryFile(tempPath);
            }
        }
    }

    public IReadOnlyList<CharacterPerformanceObservation> GetByCharacter(
        CharacterRecordId characterRecordId)
    {
        ArgumentNullException.ThrowIfNull(characterRecordId);

        lock (_sync)
        {
            return Order(_observations.Values.Where(observation =>
                    observation.CharacterRecordId == characterRecordId))
                .ToArray();
        }
    }

    public CharacterPerformanceObservationUpdateResult SetIncludeInOverview(
        GameplaySessionId gameplaySessionId,
        int segmentOrdinal,
        bool includeInOverview)
    {
        ArgumentNullException.ThrowIfNull(gameplaySessionId);
        ArgumentOutOfRangeException.ThrowIfNegative(segmentOrdinal);

        var key = new ObservationKey(gameplaySessionId.Value, segmentOrdinal);

        lock (_sync)
        {
            var finalPath = BuildFilePath(key);
            if (!_observations.TryGetValue(key, out var existing))
            {
                return UpdateResult(
                    CharacterPerformanceObservationUpdateOutcome.NotFound,
                    finalPath);
            }

            if (existing.IncludeInOverview == includeInOverview)
            {
                return UpdateResult(
                    CharacterPerformanceObservationUpdateOutcome.Unchanged,
                    finalPath);
            }

            var updated = existing with
            {
                SchemaVersion = CharacterPerformanceObservation.CurrentSchemaVersion,
                IncludeInOverview = includeInOverview
            };
            var tempPath = finalPath + "." + Guid.NewGuid().ToString("n") + ".tmp";

            try
            {
                WriteTemporaryFile(tempPath, ToDocument(updated));
                _replaceFile(tempPath, finalPath);
                _observations[key] = updated;
                PublishChanged();
                return UpdateResult(
                    CharacterPerformanceObservationUpdateOutcome.Updated,
                    finalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return UpdateResult(
                    CharacterPerformanceObservationUpdateOutcome.PersistenceFailed,
                    finalPath,
                    exception.Message);
            }
            finally
            {
                TryDeleteTemporaryFile(tempPath);
            }
        }
    }

    public CharacterPerformanceObservationDeleteResult Delete(
        GameplaySessionId gameplaySessionId,
        int segmentOrdinal)
    {
        ArgumentNullException.ThrowIfNull(gameplaySessionId);
        ArgumentOutOfRangeException.ThrowIfNegative(segmentOrdinal);

        var key = new ObservationKey(gameplaySessionId.Value, segmentOrdinal);

        lock (_sync)
        {
            if (!_observations.ContainsKey(key))
            {
                return DeleteResult(CharacterPerformanceObservationDeleteOutcome.NotFound);
            }

            try
            {
                // File.Delete is intentionally idempotent for a missing managed file. If another
                // actor already removed it, the in-memory entry is reconciled as a successful
                // deletion and subscribers receive the same single authoritative invalidation.
                _deleteFile(BuildFilePath(key));
                _observations.Remove(key);
                PublishChanged();
                return DeleteResult(CharacterPerformanceObservationDeleteOutcome.Deleted);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return DeleteResult(
                    CharacterPerformanceObservationDeleteOutcome.PersistenceFailed,
                    exception.Message);
            }
        }
    }

    public IReadOnlyList<CharacterPerformanceObservation> GetAll()
    {
        lock (_sync)
        {
            return Order(_observations.Values).ToArray();
        }
    }

    internal static string BuildFileName(GameplaySessionId sessionId, int segmentOrdinal) =>
        $"{sessionId}_{segmentOrdinal:D10}.json";

    private void LoadFromDisk()
    {
        lock (_sync)
        {
            _observations.Clear();
            _malformedFileReports.Clear();

            if (!Directory.Exists(ObservationDirectory))
            {
                return;
            }

            string[] filePaths;
            try
            {
                filePaths = Directory.GetFiles(ObservationDirectory, "*.json")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                _malformedFileReports.Add($"{ObservationDirectory}: {exception.Message}");
                return;
            }

            foreach (var filePath in filePaths)
            {
                if (!TryReadObservation(filePath, out var observation, out var error))
                {
                    _malformedFileReports.Add($"{filePath}: {error}");
                    continue;
                }

                var key = ObservationKey.From(observation);
                var expectedFileName = BuildFileName(
                    observation.GameplaySessionId,
                    observation.SegmentOrdinal);
                if (!string.Equals(
                        Path.GetFileName(filePath),
                        expectedFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _malformedFileReports.Add(
                        $"{filePath}: filename does not match the observation key.");
                    continue;
                }

                if (_observations.TryGetValue(key, out var existing))
                {
                    var detail = existing == observation
                        ? "duplicate observation key"
                        : "conflicting observation key";
                    _malformedFileReports.Add($"{filePath}: {detail}.");
                    continue;
                }

                _observations.Add(key, observation);
            }
        }
    }

    private CharacterPerformanceObservationWriteResult ResolveExistingFile(
        string filePath,
        CharacterPerformanceObservation candidate)
    {
        if (!TryReadObservation(filePath, out var existing, out var error))
        {
            return Result(
                CharacterPerformanceObservationWriteOutcome.Conflict,
                filePath,
                $"The deterministic observation path is occupied by an invalid file: {error}");
        }

        var key = ObservationKey.From(existing);
        if (key != ObservationKey.From(candidate))
        {
            return Result(
                CharacterPerformanceObservationWriteOutcome.Conflict,
                filePath,
                "The deterministic observation path contains a different observation key.");
        }

        _observations[key] = existing;
        return ResolveDuplicate(existing, candidate, filePath);
    }

    private static CharacterPerformanceObservationWriteResult ResolveDuplicate(
        CharacterPerformanceObservation existing,
        CharacterPerformanceObservation candidate,
        string filePath) =>
        HasSameCapturedContent(existing, candidate)
            ? Result(CharacterPerformanceObservationWriteOutcome.Duplicate, filePath)
            : Result(
                CharacterPerformanceObservationWriteOutcome.Conflict,
                filePath,
                "An observation with the same gameplay-session key has different content.");

    private static bool HasSameCapturedContent(
        CharacterPerformanceObservation existing,
        CharacterPerformanceObservation candidate) =>
        existing with
        {
            SchemaVersion = CharacterPerformanceObservation.CurrentSchemaVersion,
            IncludeInOverview = true
        }
        == candidate with
        {
            SchemaVersion = CharacterPerformanceObservation.CurrentSchemaVersion,
            IncludeInOverview = true
        };

    private bool TryReadObservation(
        string filePath,
        out CharacterPerformanceObservation observation,
        out string error)
    {
        observation = null!;
        error = string.Empty;

        try
        {
            var json = File.ReadAllText(filePath);
            var document = JsonSerializer.Deserialize<PersistedObservationDocument>(
                json,
                SerializerOptions);
            if (document is null)
            {
                error = "document deserialized to null";
                return false;
            }

            if (document.SchemaVersion < CharacterPerformanceObservation.MinimumSupportedSchemaVersion
                || document.SchemaVersion > CharacterPerformanceObservation.CurrentSchemaVersion)
            {
                error = $"unsupported schemaVersion {document.SchemaVersion}";
                return false;
            }

            if (!Guid.TryParse(document.GameplaySessionId, out var sessionGuid)
                || sessionGuid == Guid.Empty)
            {
                error = "gameplaySessionId is missing or invalid";
                return false;
            }

            if (!Guid.TryParse(document.CharacterRecordId, out var characterGuid)
                || characterGuid == Guid.Empty)
            {
                error = "characterRecordId is missing or invalid";
                return false;
            }

            var loaded = new CharacterPerformanceObservation
            {
                SchemaVersion = document.SchemaVersion,
                IncludeInOverview = document.IncludeInOverview ?? true,
                GameplaySessionId = GameplaySessionId.FromGuid(sessionGuid),
                SegmentOrdinal = document.SegmentOrdinal,
                CharacterRecordId = CharacterRecordId.FromGuid(characterGuid),
                StartedAtUtc = document.StartedAtUtc,
                EndedAtUtc = document.EndedAtUtc,
                DamageDealt = new CombatScaledAmount(document.DamageDealtHundredths),
                Attempts = document.Attempts,
                Hits = document.Hits,
                RolledAttempts = document.RolledAttempts,
                DisplayedChanceSumHundredths = document.DisplayedChanceSumHundredths,
                RollSumHundredths = document.RollSumHundredths,
                ForcedHits = document.ForcedHits,
                Autohits = document.Autohits,
                TotalDefeated = document.TotalDefeated,
                MyDefeats = document.MyDefeats,
                ExperienceGained = document.ExperienceGained,
                GameplayInfluenceGained = document.GameplayInfluenceGained
            };

            if (!TryValidate(loaded, out error))
            {
                return false;
            }

            observation = Normalize(loaded);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryValidate(
        CharacterPerformanceObservation observation,
        out string error)
    {
        if (observation.SchemaVersion < CharacterPerformanceObservation.MinimumSupportedSchemaVersion
            || observation.SchemaVersion > CharacterPerformanceObservation.CurrentSchemaVersion)
        {
            error = $"unsupported schemaVersion {observation.SchemaVersion}";
            return false;
        }

        if (observation.GameplaySessionId is null
            || observation.GameplaySessionId.Value == Guid.Empty)
        {
            error = "gameplaySessionId is required";
            return false;
        }

        if (observation.SegmentOrdinal < 0)
        {
            error = "segmentOrdinal must be non-negative";
            return false;
        }

        if (observation.CharacterRecordId is null
            || observation.CharacterRecordId.Value == Guid.Empty)
        {
            error = "characterRecordId is required";
            return false;
        }

        if (observation.EndedAtUtc <= observation.StartedAtUtc)
        {
            error = "endedAtUtc must be later than startedAtUtc";
            return false;
        }

        if (observation.DamageDealt.Hundredths < 0)
        {
            error = "damageDealt must be non-negative";
            return false;
        }

        if (observation.Attempts < 0
            || observation.Hits < 0
            || observation.RolledAttempts < 0
            || observation.DisplayedChanceSumHundredths < 0
            || observation.RollSumHundredths < 0
            || observation.ForcedHits < 0
            || observation.Autohits < 0)
        {
            error = "accuracy counters and sums must be non-negative";
            return false;
        }

        if (observation.Hits > observation.Attempts)
        {
            error = "hits cannot exceed attempts";
            return false;
        }

        if (observation.ForcedHits > observation.Hits)
        {
            error = "forcedHits cannot exceed hits";
            return false;
        }

        if (observation.RolledAttempts > observation.Attempts)
        {
            error = "rolledAttempts cannot exceed attempts";
            return false;
        }

        if (observation.TotalDefeated < 0 || observation.MyDefeats < 0)
        {
            error = "enemy totals must be non-negative";
            return false;
        }

        if (observation.MyDefeats > observation.TotalDefeated)
        {
            error = "myDefeats cannot exceed totalDefeated";
            return false;
        }

        if (observation.ExperienceGained < 0 || observation.GameplayInfluenceGained < 0)
        {
            error = "earnings totals must be non-negative";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static CharacterPerformanceObservation Normalize(
        CharacterPerformanceObservation observation) =>
        observation with
        {
            StartedAtUtc = observation.StartedAtUtc.ToUniversalTime(),
            EndedAtUtc = observation.EndedAtUtc.ToUniversalTime()
        };

    private static PersistedObservationDocument ToDocument(
        CharacterPerformanceObservation observation) =>
        new()
        {
            SchemaVersion = observation.SchemaVersion,
            IncludeInOverview = observation.SchemaVersion >= 2
                ? observation.IncludeInOverview
                : null,
            GameplaySessionId = observation.GameplaySessionId.ToString(),
            SegmentOrdinal = observation.SegmentOrdinal,
            CharacterRecordId = observation.CharacterRecordId.ToString(),
            StartedAtUtc = observation.StartedAtUtc,
            EndedAtUtc = observation.EndedAtUtc,
            DamageDealtHundredths = observation.DamageDealt.Hundredths,
            Attempts = observation.Attempts,
            Hits = observation.Hits,
            RolledAttempts = observation.RolledAttempts,
            DisplayedChanceSumHundredths = observation.DisplayedChanceSumHundredths,
            RollSumHundredths = observation.RollSumHundredths,
            ForcedHits = observation.ForcedHits,
            Autohits = observation.Autohits,
            TotalDefeated = observation.TotalDefeated,
            MyDefeats = observation.MyDefeats,
            ExperienceGained = observation.ExperienceGained,
            GameplayInfluenceGained = observation.GameplayInfluenceGained
        };

    private static IEnumerable<CharacterPerformanceObservation> Order(
        IEnumerable<CharacterPerformanceObservation> observations) =>
        observations
            .OrderBy(observation => observation.StartedAtUtc)
            .ThenBy(observation => observation.GameplaySessionId.Value)
            .ThenBy(observation => observation.SegmentOrdinal);

    private string BuildFilePath(ObservationKey key) =>
        Path.Combine(
            ObservationDirectory,
            BuildFileName(GameplaySessionId.FromGuid(key.GameplaySessionId), key.SegmentOrdinal));

    private static void WriteAtomically(
        string tempPath,
        string finalPath,
        PersistedObservationDocument document)
    {
        WriteTemporaryFile(tempPath, document);
        File.Move(tempPath, finalPath, overwrite: false);
    }

    private static void WriteTemporaryFile(
        string tempPath,
        PersistedObservationDocument document)
    {
        var json = JsonSerializer.Serialize(document, SerializerOptions);
        using (var stream = new FileStream(
                   tempPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
    }

    private static void ReplaceFile(string tempPath, string finalPath) =>
        File.Move(tempPath, finalPath, overwrite: true);

    private static void TryDeleteTemporaryFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
        }
    }

    private static CharacterPerformanceObservationWriteResult Result(
        CharacterPerformanceObservationWriteOutcome outcome,
        string? filePath = null,
        string? detail = null) =>
        new()
        {
            Outcome = outcome,
            FilePath = filePath,
            Detail = detail
        };

    private static CharacterPerformanceObservationUpdateResult UpdateResult(
        CharacterPerformanceObservationUpdateOutcome outcome,
        string? filePath = null,
        string? detail = null) =>
        new()
        {
            Outcome = outcome,
            FilePath = filePath,
            Detail = detail
        };

    private static CharacterPerformanceObservationDeleteResult DeleteResult(
        CharacterPerformanceObservationDeleteOutcome outcome,
        string? detail = null) =>
        new()
        {
            Outcome = outcome,
            Detail = detail
        };

    private void PublishChanged()
    {
        var handlers = Changed?.GetInvocationList();
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
                // Observation persistence remains authoritative even if a UI invalidation
                // subscriber is shutting down or otherwise cannot accept the notification.
            }
        }
    }

    private readonly record struct ObservationKey(Guid GameplaySessionId, int SegmentOrdinal)
    {
        public static ObservationKey From(CharacterPerformanceObservation observation) =>
            new(observation.GameplaySessionId.Value, observation.SegmentOrdinal);
    }

    private sealed class PersistedObservationDocument
    {
        public required int SchemaVersion { get; set; }

        public bool? IncludeInOverview { get; set; }

        public required string GameplaySessionId { get; set; }

        public required int SegmentOrdinal { get; set; }

        public required string CharacterRecordId { get; set; }

        public required DateTimeOffset StartedAtUtc { get; set; }

        public required DateTimeOffset EndedAtUtc { get; set; }

        public required long DamageDealtHundredths { get; set; }

        public required long Attempts { get; set; }

        public required long Hits { get; set; }

        public required long RolledAttempts { get; set; }

        public required long DisplayedChanceSumHundredths { get; set; }

        public required long RollSumHundredths { get; set; }

        public required long ForcedHits { get; set; }

        public required long Autohits { get; set; }

        public required long TotalDefeated { get; set; }

        public required long MyDefeats { get; set; }

        public required long ExperienceGained { get; set; }

        public required long GameplayInfluenceGained { get; set; }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Persists versioned session documents under <c>%LocalAppData%\CoH Analytics\Sessions\</c> with
/// rolling retention for recent Live and Tracked sessions and permanent Saved promotion.
/// </summary>
public sealed class SessionStore : ISessionStore
{
    public const int RecentSessionRetentionLimit = 5;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly object _sync = new();
    private readonly string _liveDirectory;
    private readonly string _trackedDirectory;
    private readonly string _savedDirectory;
    private readonly string _legacyBenchmarkDirectory;
    private readonly TimeProvider _timeProvider;
    private readonly List<string> _malformedFileReports = [];
    private readonly List<string> _legacyBenchmarkFilePaths;
    private Dictionary<Guid, PersistedSessionDocument> _liveSessions = [];
    private Dictionary<Guid, PersistedSessionDocument> _trackedSessions = [];
    private Dictionary<Guid, PersistedSessionDocument> _savedSessions = [];

    public SessionStore(string? dataDirectory = null, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _liveDirectory = ApplicationDataPaths.GetLiveSessionsDirectory(dataDirectory);
        _trackedDirectory = ApplicationDataPaths.GetTrackedSessionsDirectory(dataDirectory);
        _savedDirectory = ApplicationDataPaths.GetSavedSessionsDirectory(dataDirectory);
        _legacyBenchmarkDirectory = ApplicationDataPaths.GetLegacyTrackedBenchmarkDirectory(dataDirectory);
        SessionRootDirectory = ApplicationDataPaths.GetSessionsRoot(dataDirectory);
        _legacyBenchmarkFilePaths = DiscoverLegacyBenchmarkFiles();
        ReloadFromDisk();
    }

    public string SessionRootDirectory { get; }

    public IReadOnlyList<string> LegacyBenchmarkFilePaths => _legacyBenchmarkFilePaths;

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

    public IReadOnlyList<PersistedSessionDocument> GetRecentLiveSessions()
    {
        lock (_sync)
        {
            return OrderRecent(_liveSessions.Values).ToArray();
        }
    }

    public IReadOnlyList<PersistedSessionDocument> GetRecentTrackedSessions()
    {
        lock (_sync)
        {
            return OrderRecent(_trackedSessions.Values).ToArray();
        }
    }

    public IReadOnlyList<PersistedSessionDocument> GetSavedSessions()
    {
        lock (_sync)
        {
            return _savedSessions.Values
                .OrderByDescending(session => session.SavedAtUtc ?? session.EndedAtUtc)
                .ToArray();
        }
    }

    public PersistedSessionDocument? GetSession(Guid sessionId)
    {
        lock (_sync)
        {
            if (_savedSessions.TryGetValue(sessionId, out var saved))
            {
                return saved;
            }

            if (_liveSessions.TryGetValue(sessionId, out var live))
            {
                return live;
            }

            return _trackedSessions.TryGetValue(sessionId, out var tracked) ? tracked : null;
        }
    }

    public string PersistCompletedLiveSession(PersistedSessionDocument document) =>
        PersistRecentSession(document, SessionType.Live, _liveDirectory, _liveSessions);

    public string PersistCompletedTrackedSession(PersistedSessionDocument document) =>
        PersistRecentSession(document, SessionType.Tracked, _trackedDirectory, _trackedSessions);

    public SessionSaveResult SaveSession(PersistedSessionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (_sync)
        {
            var normalized = NormalizeSavedDocument(document);
            if (normalized.SessionType != SessionType.Live && normalized.SessionType != SessionType.Tracked)
            {
                throw new ArgumentException("Saved sessions must retain a Live or Tracked session type.", nameof(document));
            }

            if (_savedSessions.TryGetValue(normalized.SessionId, out var existing)
                && DocumentsAreEquivalent(existing, normalized))
            {
                return new SessionSaveResult(SessionSaveOutcome.DuplicateSkipped, FindSavedFilePath(existing.SessionId));
            }

            var savedDocument = normalized with
            {
                SavedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime()
            };

            Directory.CreateDirectory(_savedDirectory);
            var filePath = WriteDocument(_savedDirectory, savedDocument);
            _savedSessions[savedDocument.SessionId] = savedDocument;
            return new SessionSaveResult(SessionSaveOutcome.Saved, filePath);
        }
    }

    public void DeleteSavedSession(Guid sessionId)
    {
        lock (_sync)
        {
            if (!_savedSessions.Remove(sessionId))
            {
                return;
            }

            var filePath = FindSavedFilePath(sessionId);
            if (filePath is not null && File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    private string PersistRecentSession(
        PersistedSessionDocument document,
        SessionType expectedType,
        string directory,
        Dictionary<Guid, PersistedSessionDocument> cache)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (_sync)
        {
            var normalized = NormalizeDocument(document, expectedType);
            Directory.CreateDirectory(directory);

            if (cache.TryGetValue(normalized.SessionId, out var existing)
                && DocumentsAreEquivalent(existing, normalized))
            {
                return FindRecentFilePath(directory, normalized.SessionId)
                    ?? WriteDocument(directory, normalized);
            }

            var filePath = WriteDocument(directory, normalized);
            cache[normalized.SessionId] = normalized;
            EnforceRecentRetention(directory, cache);
            return filePath;
        }
    }

    private void ReloadFromDisk()
    {
        lock (_sync)
        {
            _malformedFileReports.Clear();
            _liveSessions = LoadDirectory(_liveDirectory, SessionType.Live);
            _trackedSessions = LoadDirectory(_trackedDirectory, SessionType.Tracked);
            _savedSessions = LoadDirectory(_savedDirectory, sessionType: null, requireSavedAt: true);
        }
    }

    private Dictionary<Guid, PersistedSessionDocument> LoadDirectory(
        string directory,
        SessionType? sessionType,
        bool requireSavedAt = false)
    {
        var sessions = new Dictionary<Guid, PersistedSessionDocument>();
        if (!Directory.Exists(directory))
        {
            return sessions;
        }

        foreach (var filePath in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (!TryLoadDocument(filePath, out var document, out var error))
            {
                _malformedFileReports.Add($"{filePath}: {error}");
                continue;
            }

            if (sessionType is not null && document.SessionType != sessionType)
            {
                _malformedFileReports.Add(
                    $"{filePath}: expected sessionType '{sessionType}', found '{document.SessionType}'.");
                continue;
            }

            if (requireSavedAt && document.SavedAtUtc is null)
            {
                _malformedFileReports.Add($"{filePath}: saved session is missing savedAtUtc.");
                continue;
            }

            if (!sessions.TryGetValue(document.SessionId, out var existing)
                || document.EndedAtUtc > existing.EndedAtUtc)
            {
                sessions[document.SessionId] = document;
            }
        }

        return sessions;
    }

    private bool TryLoadDocument(
        string filePath,
        out PersistedSessionDocument document,
        out string error)
    {
        document = null!;
        error = string.Empty;

        try
        {
            var json = File.ReadAllText(filePath);
            var loaded = JsonSerializer.Deserialize<PersistedSessionDocument>(json, SerializerOptions);
            if (loaded is null)
            {
                error = "document deserialized to null";
                return false;
            }

            if (loaded.SchemaVersion < PersistedSessionDocument.MinimumSupportedSchemaVersion
                || loaded.SchemaVersion > PersistedSessionDocument.CurrentSchemaVersion)
            {
                error = $"unsupported schemaVersion {loaded.SchemaVersion}";
                return false;
            }

            if (loaded.SessionId == Guid.Empty)
            {
                error = "sessionId is missing";
                return false;
            }

            document = NormalizeLoadedDocument(loaded);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void EnforceRecentRetention(string directory, Dictionary<Guid, PersistedSessionDocument> cache)
    {
        var excess = OrderRecent(cache.Values)
            .Skip(RecentSessionRetentionLimit)
            .ToArray();

        foreach (var session in excess)
        {
            cache.Remove(session.SessionId);
            var filePath = FindRecentFilePath(directory, session.SessionId);
            if (filePath is not null && File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    private static IEnumerable<PersistedSessionDocument> OrderRecent(
        IEnumerable<PersistedSessionDocument> sessions) =>
        sessions.OrderByDescending(session => session.EndedAtUtc);

    private string WriteDocument(string directory, PersistedSessionDocument document)
    {
        var fileName = BuildFileName(document);
        var filePath = Path.Combine(directory, fileName);
        var tempPath = filePath + ".tmp";
        var json = JsonSerializer.Serialize(document, SerializerOptions);

        File.WriteAllText(tempPath, json);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        File.Move(tempPath, filePath, overwrite: false);
        return filePath;
    }

    internal static string BuildFileName(PersistedSessionDocument document)
    {
        var timestamp = document.EndedAtUtc.ToLocalTime();
        var shortId = document.SessionId.ToString("N")[..8];
        var typeLabel = document.SessionType.ToString();
        return $"{timestamp:yyyy-MM-dd_HH-mm-ss}_{typeLabel}_{shortId}.json";
    }

    private string? FindRecentFilePath(string directory, Guid sessionId)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var suffix = $"_{sessionId.ToString("N")[..8]}.json";
        return Directory.EnumerateFiles(directory, "*.json")
            .FirstOrDefault(path => Path.GetFileName(path).EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private string? FindSavedFilePath(Guid sessionId) => FindRecentFilePath(_savedDirectory, sessionId);

    private List<string> DiscoverLegacyBenchmarkFiles()
    {
        if (!Directory.Exists(_legacyBenchmarkDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_legacyBenchmarkDirectory, "*.json").ToList();
    }

    private static PersistedSessionDocument NormalizeSavedDocument(PersistedSessionDocument document)
    {
        if (document.SessionId == Guid.Empty)
        {
            throw new ArgumentException("sessionId is required.", nameof(document));
        }

        return NormalizeEconomyDocument(document with
        {
            SchemaVersion = PersistedSessionDocument.CurrentSchemaVersion,
            StartedAtUtc = document.StartedAtUtc.ToUniversalTime(),
            EndedAtUtc = document.EndedAtUtc.ToUniversalTime(),
            SavedAtUtc = document.SavedAtUtc?.ToUniversalTime()
        });
    }

    private static PersistedSessionDocument NormalizeDocument(
        PersistedSessionDocument document,
        SessionType expectedType)
    {
        if (document.SessionId == Guid.Empty)
        {
            throw new ArgumentException("sessionId is required.", nameof(document));
        }

        if (document.SessionType != expectedType)
        {
            throw new ArgumentException(
                $"Expected sessionType '{expectedType}', found '{document.SessionType}'.",
                nameof(document));
        }

        return NormalizeEconomyDocument(document with
        {
            SchemaVersion = PersistedSessionDocument.CurrentSchemaVersion,
            StartedAtUtc = document.StartedAtUtc.ToUniversalTime(),
            EndedAtUtc = document.EndedAtUtc.ToUniversalTime(),
            SavedAtUtc = document.SavedAtUtc?.ToUniversalTime()
        });
    }

    private static PersistedSessionDocument NormalizeLoadedDocument(PersistedSessionDocument document) =>
        NormalizeEconomyDocument(document);

    private static PersistedSessionDocument NormalizeEconomyDocument(PersistedSessionDocument document) =>
        document with
        {
            Earnings = document.Earnings ?? new SessionEarnings(),
            Currencies = NormalizeCurrencyTotals(document.Currencies),
            Loot = NormalizeLootTotals(document.Loot),
            Context = document.Context ?? new SessionContextMetadata()
        };

    private static IReadOnlyList<PersistedSessionCurrencyTotal> NormalizeCurrencyTotals(
        IReadOnlyList<PersistedSessionCurrencyTotal>? currencies) =>
        currencies is null
            ? []
            : currencies
                .Where(currency => !string.IsNullOrWhiteSpace(currency.Name) && currency.Quantity > 0)
                .Select(currency => currency with { Name = currency.Name.Trim() })
                .OrderBy(currency => currency.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static PersistedSessionLootTotals NormalizeLootTotals(PersistedSessionLootTotals? loot) =>
        new()
        {
            Salvage = NormalizeLootItems(loot?.Salvage),
            Recipes = NormalizeLootItems(loot?.Recipes),
            Enhancements = NormalizeLootItems(loot?.Enhancements),
            Inspirations = NormalizeLootItems(loot?.Inspirations)
        };

    private static IReadOnlyList<PersistedSessionLootItem> NormalizeLootItems(
        IReadOnlyList<PersistedSessionLootItem>? items) =>
        items is null
            ? []
            : items
                .Where(item => !string.IsNullOrWhiteSpace(item.Name) && item.Quantity > 0)
                .Select(item => item with
                {
                    Name = item.Name.Trim(),
                    CatalogId = string.IsNullOrWhiteSpace(item.CatalogId) ? null : item.CatalogId.Trim()
                })
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

    internal static bool DocumentsAreEquivalent(
        PersistedSessionDocument left,
        PersistedSessionDocument right) =>
        left.SessionId == right.SessionId
        && left.SessionType == right.SessionType
        && left.StartedAtUtc == right.StartedAtUtc
        && left.EndedAtUtc == right.EndedAtUtc
        && left.DurationSeconds == right.DurationSeconds
        && left.Earnings.Experience == right.Earnings.Experience
        && left.Earnings.Influence == right.Earnings.Influence
        && string.Equals(left.Account, right.Account, StringComparison.Ordinal)
        && string.Equals(left.Character, right.Character, StringComparison.Ordinal)
        && CurrencyTotalsEquivalent(left.Currencies, right.Currencies)
        && LootTotalsEquivalent(left.Loot, right.Loot);

    private static bool CurrencyTotalsEquivalent(
        IReadOnlyList<PersistedSessionCurrencyTotal> left,
        IReadOnlyList<PersistedSessionCurrencyTotal> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].Name, right[index].Name, StringComparison.Ordinal)
                || left[index].Quantity != right[index].Quantity)
            {
                return false;
            }
        }

        return true;
    }

    private static bool LootTotalsEquivalent(
        PersistedSessionLootTotals left,
        PersistedSessionLootTotals right) =>
        LootItemsEquivalent(left.Salvage, right.Salvage)
        && LootItemsEquivalent(left.Recipes, right.Recipes)
        && LootItemsEquivalent(left.Enhancements, right.Enhancements)
        && LootItemsEquivalent(left.Inspirations, right.Inspirations);

    private static bool LootItemsEquivalent(
        IReadOnlyList<PersistedSessionLootItem> left,
        IReadOnlyList<PersistedSessionLootItem> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].Name, right[index].Name, StringComparison.Ordinal)
                || left[index].Quantity != right[index].Quantity
                || !string.Equals(left[index].CatalogId, right[index].CatalogId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

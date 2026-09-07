using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Persists a small, curated history of user-facing application lifecycle events.
/// Raw parser lines and gameplay telemetry do not belong in this store.
/// </summary>
public sealed class ApplicationActivityLogService
{
    public const int CurrentSchemaVersion = 1;
    public const int DefaultRetentionLimit = 100;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _activityLogPath;
    private readonly int _retentionLimit;
    private readonly TimeProvider _timeProvider;
    private List<ApplicationActivityEntry> _entries;

    public ApplicationActivityLogService(
        string? dataDirectory = null,
        int retentionLimit = DefaultRetentionLimit,
        TimeProvider? timeProvider = null)
    {
        if (retentionLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionLimit));
        }

        var root = dataDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CoH Analytics");

        _activityLogPath = Path.Combine(root, "activity-history.json");
        _retentionLimit = retentionLimit;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _entries = LoadEntries();
    }

    public event EventHandler? Changed;

    public string ActivityLogPath => _activityLogPath;

    public IReadOnlyList<ApplicationActivityEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }
    }

    public bool Record(
        string eventType,
        string displayMessage,
        DateTimeOffset? timestamp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayMessage);

        var normalizedType = eventType.Trim();
        var normalizedMessage = displayMessage.Trim();
        var entry = new ApplicationActivityEntry(
            (timestamp ?? _timeProvider.GetUtcNow()).ToUniversalTime(),
            normalizedType,
            normalizedMessage);

        lock (_sync)
        {
            var previous = _entries.Count == 0 ? null : _entries[^1];
            if (previous is not null
                && string.Equals(previous.EventType, entry.EventType, StringComparison.Ordinal)
                && string.Equals(previous.DisplayMessage, entry.DisplayMessage, StringComparison.Ordinal))
            {
                return false;
            }

            _entries.Add(entry);
            if (_entries.Count > _retentionLimit)
            {
                _entries.RemoveRange(0, _entries.Count - _retentionLimit);
            }

            PersistNoThrow();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private List<ApplicationActivityEntry> LoadEntries()
    {
        if (!File.Exists(_activityLogPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_activityLogPath);
            var document = JsonSerializer.Deserialize<PersistedActivityDocument>(json, SerializerOptions);
            if (document is null || document.SchemaVersion != CurrentSchemaVersion)
            {
                return [];
            }

            return document.Entries
                .Where(entry =>
                    !string.IsNullOrWhiteSpace(entry.EventType)
                    && !string.IsNullOrWhiteSpace(entry.DisplayMessage))
                .OrderBy(entry => entry.Timestamp)
                .TakeLast(_retentionLimit)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private void PersistNoThrow()
    {
        var tempPath = _activityLogPath + ".tmp";

        try
        {
            var directory = Path.GetDirectoryName(_activityLogPath)!;
            Directory.CreateDirectory(directory);

            var document = new PersistedActivityDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                Entries = _entries.ToList()
            };
            var json = JsonSerializer.Serialize(document, SerializerOptions);

            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _activityLogPath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    private sealed class PersistedActivityDocument
    {
        public int SchemaVersion { get; set; }

        public List<ApplicationActivityEntry> Entries { get; set; } = [];
    }
}

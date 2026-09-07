using System.Text.Json;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class SessionStoreTests
{
    [Fact]
    public void PersistCompletedLiveSession_writes_under_live_directory()
    {
        var directory = CreateDataDirectory();
        var store = new SessionStore(directory);
        var sessionId = Guid.NewGuid();
        var startedAt = new DateTimeOffset(2026, 8, 9, 1, 0, 0, TimeSpan.Zero);
        var endedAt = startedAt.AddHours(1);

        try
        {
            var path = store.PersistCompletedLiveSession(CreateDocument(
                sessionId,
                SessionType.Live,
                startedAt,
                endedAt,
                experience: 12_500,
                influence: 2_400));

            Assert.StartsWith(ApplicationDataPaths.GetLiveSessionsDirectory(directory), path);
            Assert.True(File.Exists(path));
            Assert.Single(store.GetRecentLiveSessions());
            Assert.Equal(sessionId, store.GetRecentLiveSessions()[0].SessionId);
            Assert.Equal(12_500, store.GetRecentLiveSessions()[0].Earnings.Experience);
            Assert.Equal(2_400, store.GetRecentLiveSessions()[0].Earnings.Influence);
            Assert.Equal(3_600, store.GetRecentLiveSessions()[0].DurationSeconds);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void PersistCompletedTrackedSession_writes_under_tracked_directory()
    {
        var directory = CreateDataDirectory();
        var store = new SessionStore(directory);
        var sessionId = Guid.NewGuid();
        var startedAt = new DateTimeOffset(2026, 8, 9, 2, 0, 0, TimeSpan.Zero);
        var endedAt = startedAt.AddMinutes(30);

        try
        {
            var path = store.PersistCompletedTrackedSession(CreateDocument(
                sessionId,
                SessionType.Tracked,
                startedAt,
                endedAt,
                experience: 4_000,
                influence: 800));

            Assert.StartsWith(ApplicationDataPaths.GetTrackedSessionsDirectory(directory), path);
            Assert.True(File.Exists(path));
            Assert.Single(store.GetRecentTrackedSessions());
            Assert.Equal(SessionType.Tracked, store.GetRecentTrackedSessions()[0].SessionType);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Live_retention_keeps_latest_five_completed_sessions()
    {
        var directory = CreateDataDirectory();
        var store = new SessionStore(directory);
        var baseTime = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);

        try
        {
            for (var index = 0; index < 6; index++)
            {
                var startedAt = baseTime.AddHours(index);
                store.PersistCompletedLiveSession(CreateDocument(
                    Guid.NewGuid(),
                    SessionType.Live,
                    startedAt,
                    startedAt.AddMinutes(45),
                    experience: index * 100,
                    influence: index * 10));
            }

            var recent = store.GetRecentLiveSessions();
            Assert.Equal(5, recent.Count);
            Assert.Equal(500, recent[0].Earnings.Experience);
            Assert.Equal(100, recent[^1].Earnings.Experience);
            Assert.Equal(5, Directory.GetFiles(ApplicationDataPaths.GetLiveSessionsDirectory(directory)).Length);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Tracked_retention_keeps_latest_five_completed_sessions()
    {
        var directory = CreateDataDirectory();
        var store = new SessionStore(directory);
        var baseTime = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);

        try
        {
            for (var index = 0; index < 6; index++)
            {
                var startedAt = baseTime.AddHours(index);
                store.PersistCompletedTrackedSession(CreateDocument(
                    Guid.NewGuid(),
                    SessionType.Tracked,
                    startedAt,
                    startedAt.AddMinutes(20),
                    experience: index * 50,
                    influence: index * 5));
            }

            var recent = store.GetRecentTrackedSessions();
            Assert.Equal(5, recent.Count);
            Assert.Equal(250, recent[0].Earnings.Experience);
            Assert.Equal(50, recent[^1].Earnings.Experience);
            Assert.Equal(5, Directory.GetFiles(ApplicationDataPaths.GetTrackedSessionsDirectory(directory)).Length);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SaveSession_promotes_to_saved_and_survives_recent_retention()
    {
        var directory = CreateDataDirectory();
        var store = new SessionStore(directory);
        var savedSessionId = Guid.NewGuid();
        var baseTime = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);

        try
        {
            var savedDocument = CreateDocument(
                savedSessionId,
                SessionType.Live,
                baseTime,
                baseTime.AddMinutes(30),
                experience: 9_999,
                influence: 1_111);

            store.SaveSession(savedDocument);
            Assert.Single(store.GetSavedSessions());

            for (var index = 0; index < 6; index++)
            {
                var startedAt = baseTime.AddHours(index + 1);
                store.PersistCompletedLiveSession(CreateDocument(
                    Guid.NewGuid(),
                    SessionType.Live,
                    startedAt,
                    startedAt.AddMinutes(10),
                    experience: index,
                    influence: index));
            }

            Assert.Single(store.GetSavedSessions());
            Assert.Equal(savedSessionId, store.GetSavedSessions()[0].SessionId);
            Assert.NotNull(store.GetSavedSessions()[0].SavedAtUtc);
            Assert.Equal(5, store.GetRecentLiveSessions().Count);
            Assert.NotNull(store.GetSession(savedSessionId));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SaveSession_skips_duplicate_unchanged_session()
    {
        var directory = CreateDataDirectory();
        var store = new SessionStore(directory);
        var sessionId = Guid.NewGuid();
        var startedAt = new DateTimeOffset(2026, 8, 9, 3, 0, 0, TimeSpan.Zero);
        var document = CreateDocument(
            sessionId,
            SessionType.Tracked,
            startedAt,
            startedAt.AddMinutes(15),
            experience: 1_000,
            influence: 200);

        try
        {
            var first = store.SaveSession(document);
            var second = store.SaveSession(document);

            Assert.Equal(SessionSaveOutcome.Saved, first.Outcome);
            Assert.Equal(SessionSaveOutcome.DuplicateSkipped, second.Outcome);
            Assert.Single(store.GetSavedSessions());
            Assert.Single(Directory.GetFiles(ApplicationDataPaths.GetSavedSessionsDirectory(directory)));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Restart_reloads_recent_and_saved_sessions()
    {
        var directory = CreateDataDirectory();
        var liveId = Guid.NewGuid();
        var trackedId = Guid.NewGuid();
        var savedId = Guid.NewGuid();
        var startedAt = new DateTimeOffset(2026, 8, 9, 4, 0, 0, TimeSpan.Zero);

        try
        {
            var firstStore = new SessionStore(directory);
            firstStore.PersistCompletedLiveSession(CreateDocument(
                liveId,
                SessionType.Live,
                startedAt,
                startedAt.AddMinutes(40),
                experience: 500,
                influence: 50));
            firstStore.PersistCompletedTrackedSession(CreateDocument(
                trackedId,
                SessionType.Tracked,
                startedAt.AddHours(1),
                startedAt.AddHours(1).AddMinutes(10),
                experience: 250,
                influence: 25));
            firstStore.SaveSession(CreateDocument(
                savedId,
                SessionType.Live,
                startedAt.AddHours(2),
                startedAt.AddHours(2).AddMinutes(20),
                experience: 750,
                influence: 75));

            var reloaded = new SessionStore(directory);

            Assert.Equal(liveId, reloaded.GetRecentLiveSessions().Single().SessionId);
            Assert.Equal(trackedId, reloaded.GetRecentTrackedSessions().Single().SessionId);
            Assert.Equal(savedId, reloaded.GetSavedSessions().Single().SessionId);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Malformed_json_does_not_prevent_valid_sessions_from_loading()
    {
        var directory = CreateDataDirectory();
        var validId = Guid.NewGuid();
        var startedAt = new DateTimeOffset(2026, 8, 9, 5, 0, 0, TimeSpan.Zero);

        try
        {
            Directory.CreateDirectory(ApplicationDataPaths.GetLiveSessionsDirectory(directory));
            File.WriteAllText(
                Path.Combine(ApplicationDataPaths.GetLiveSessionsDirectory(directory), "broken.json"),
                "{ not valid json");

            var writer = new SessionStore(directory);
            writer.PersistCompletedLiveSession(CreateDocument(
                validId,
                SessionType.Live,
                startedAt,
                startedAt.AddMinutes(5),
                experience: 100,
                influence: 10));

            var store = new SessionStore(directory);

            Assert.Single(store.GetRecentLiveSessions());
            Assert.Equal(validId, store.GetRecentLiveSessions()[0].SessionId);
            Assert.Contains(store.MalformedFileReports, report => report.Contains("broken.json"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void BuildFileName_uses_readable_timestamp_type_and_short_session_id()
    {
        var sessionId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var document = CreateDocument(
            sessionId,
            SessionType.Live,
            new DateTimeOffset(2026, 8, 9, 5, 32, 14, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 9, 6, 46, 2, TimeSpan.Zero),
            experience: 1,
            influence: 1);

        var fileName = SessionStore.BuildFileName(document);

        Assert.Matches(@"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}_Live_12345678\.json$", fileName);
    }

    [Fact]
    public void Persisted_json_uses_raw_numeric_fields()
    {
        var directory = CreateDataDirectory();
        var store = new SessionStore(directory);
        var startedAt = new DateTimeOffset(2026, 8, 9, 6, 0, 0, TimeSpan.Zero);

        try
        {
            var path = store.PersistCompletedTrackedSession(CreateDocument(
                Guid.NewGuid(),
                SessionType.Tracked,
                startedAt,
                startedAt.AddSeconds(930),
                experience: 12_500,
                influence: 2_400));

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("tracked", root.GetProperty("sessionType").GetString());
            Assert.Equal(930, root.GetProperty("durationSeconds").GetInt64());
            Assert.Equal(12_500, root.GetProperty("earnings").GetProperty("experience").GetInt64());
            Assert.Equal(2_400, root.GetProperty("earnings").GetProperty("influence").GetInt64());
            Assert.False(root.TryGetProperty("xpPerHour", out _));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Loads_v1_session_with_empty_currency_and_loot_collections()
    {
        var directory = CreateDataDirectory();
        var liveDirectory = ApplicationDataPaths.GetLiveSessionsDirectory(directory);
        Directory.CreateDirectory(liveDirectory);
        var sessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var path = Path.Combine(liveDirectory, "2026-08-08_12-00-00_Live_aaaaaaaa.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 1,
              "sessionId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
              "sessionType": "live",
              "account": "TestAccount",
              "character": "Example Hero",
              "startedAtUtc": "2026-08-08T12:00:00+00:00",
              "endedAtUtc": "2026-08-08T13:00:00+00:00",
              "durationSeconds": 3600,
              "earnings": {
                "experience": 1000,
                "influence": 200
              }
            }
            """);

        try
        {
            var store = new SessionStore(directory);
            var loaded = Assert.Single(store.GetRecentLiveSessions());
            Assert.Equal(sessionId, loaded.SessionId);
            Assert.Equal(1, loaded.SchemaVersion);
            Assert.Equal(1000, loaded.Earnings.Experience);
            Assert.Empty(loaded.Currencies);
            Assert.Empty(loaded.Loot.Salvage);
            Assert.Empty(loaded.Loot.Recipes);
            Assert.Empty(loaded.Loot.Enhancements);
            Assert.Empty(loaded.Loot.Inspirations);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void PersistCompletedLiveSession_persists_currencies_and_loot()
    {
        var directory = CreateDataDirectory();
        var store = new SessionStore(directory);
        var startedAt = new DateTimeOffset(2026, 8, 9, 7, 0, 0, TimeSpan.Zero);

        try
        {
            var path = store.PersistCompletedLiveSession(CreateDocument(
                Guid.NewGuid(),
                SessionType.Live,
                startedAt,
                startedAt.AddMinutes(30),
                experience: 50_000,
                influence: 10_000,
                currencies:
                [
                    new PersistedSessionCurrencyTotal { Name = "Reward Merit", Quantity = 20 },
                    new PersistedSessionCurrencyTotal { Name = "Incarnate Thread", Quantity = 5 }
                ],
                loot: new PersistedSessionLootTotals
                {
                    Salvage = [new PersistedSessionLootItem { Name = "Fortune", Quantity = 2 }],
                    Recipes = [new PersistedSessionLootItem { Name = "Invention: Endurance Red. (Recipe)", Quantity = 1 }],
                    Enhancements = [new PersistedSessionLootItem { Name = "Energy Weapon", Quantity = 1 }],
                    Inspirations = [new PersistedSessionLootItem { Name = "Luck", Quantity = 3 }]
                }));

            using var stream = File.OpenRead(path);
            using var json = JsonDocument.Parse(stream);
            Assert.Equal(2, json.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(2, json.RootElement.GetProperty("currencies").GetArrayLength());
            Assert.Equal(2, json.RootElement.GetProperty("loot").GetProperty("salvage")[0].GetProperty("quantity").GetInt64());

            var reloaded = new SessionStore(directory).GetRecentLiveSessions().Single();
            Assert.Equal(2, reloaded.Currencies.Count);
            Assert.Equal("Fortune", reloaded.Loot.Salvage.Single().Name);
            Assert.Equal(1, reloaded.Loot.Recipes.Single().Quantity);
            Assert.Equal("Energy Weapon", reloaded.Loot.Enhancements.Single().Name);
            Assert.Equal(3, reloaded.Loot.Inspirations.Single().Quantity);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SaveSession_promotion_preserves_economy_and_loot()
    {
        var directory = CreateDataDirectory();
        var store = new SessionStore(directory);
        var startedAt = new DateTimeOffset(2026, 8, 9, 8, 0, 0, TimeSpan.Zero);
        var document = CreateDocument(
            Guid.NewGuid(),
            SessionType.Tracked,
            startedAt,
            startedAt.AddMinutes(12),
            experience: 9_000,
            influence: 1_500,
            currencies: [new PersistedSessionCurrencyTotal { Name = "Astral Merit", Quantity = 1 }],
            loot: new PersistedSessionLootTotals
            {
                Salvage = [new PersistedSessionLootItem { Name = "Temporal Analyzer", Quantity = 4 }]
            });

        try
        {
            var result = store.SaveSession(document);
            Assert.Equal(SessionSaveOutcome.Saved, result.Outcome);

            var saved = Assert.Single(store.GetSavedSessions());
            Assert.Equal(2, saved.SchemaVersion);
            Assert.Equal("Astral Merit", saved.Currencies.Single().Name);
            Assert.Equal(4, saved.Loot.Salvage.Single().Quantity);

            var reloaded = Assert.Single(new SessionStore(directory).GetSavedSessions());
            Assert.Equal("Astral Merit", reloaded.Currencies.Single().Name);
            Assert.Equal("Temporal Analyzer", reloaded.Loot.Salvage.Single().Name);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static PersistedSessionDocument CreateDocument(
        Guid sessionId,
        SessionType sessionType,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc,
        long experience,
        long influence,
        IReadOnlyList<PersistedSessionCurrencyTotal>? currencies = null,
        PersistedSessionLootTotals? loot = null) =>
        new()
        {
            SessionId = sessionId,
            SessionType = sessionType,
            Account = "TestAccount",
            Character = "Example Hero",
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = endedAtUtc,
            DurationSeconds = (long)Math.Round((endedAtUtc - startedAtUtc).TotalSeconds),
            Earnings = new SessionEarnings
            {
                Experience = experience,
                Influence = influence
            },
            Currencies = currencies ?? [],
            Loot = loot ?? new PersistedSessionLootTotals()
        };

    private static string CreateDataDirectory() =>
        Path.Combine(Path.GetTempPath(), "coh-analytics-session-test-" + Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }
}

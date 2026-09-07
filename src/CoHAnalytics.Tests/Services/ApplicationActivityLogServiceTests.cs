using System.Text.Json;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class ApplicationActivityLogServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 20, 31, 0, TimeSpan.Zero);

    [Fact]
    public void Recorded_activity_persists_and_reloads()
    {
        var directory = CreateDataDirectory();
        try
        {
            var writer = new ApplicationActivityLogService(directory);
            writer.Record("homecoming.detected", "Homecoming detected", Now);

            var reader = new ApplicationActivityLogService(directory);

            var entry = Assert.Single(reader.Entries);
            Assert.Equal(Now, entry.Timestamp);
            Assert.Equal("homecoming.detected", entry.EventType);
            Assert.Equal("Homecoming detected", entry.DisplayMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Consecutive_duplicate_type_and_message_are_suppressed()
    {
        var directory = CreateDataDirectory();
        try
        {
            var service = new ApplicationActivityLogService(directory);

            Assert.True(service.Record("application.status", "Awaiting Homecoming", Now));
            Assert.False(service.Record("application.status", "Awaiting Homecoming", Now.AddMinutes(1)));
            Assert.True(service.Record("application.status", "Monitoring active", Now.AddMinutes(2)));

            Assert.Equal(2, service.Entries.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Retention_keeps_only_the_newest_entries()
    {
        var directory = CreateDataDirectory();
        try
        {
            var service = new ApplicationActivityLogService(directory, retentionLimit: 3);
            for (var index = 1; index <= 5; index++)
            {
                service.Record($"event.{index}", $"Message {index}", Now.AddMinutes(index));
            }

            Assert.Equal(3, service.Entries.Count);
            Assert.Equal("Message 3", service.Entries[0].DisplayMessage);
            Assert.Equal("Message 5", service.Entries[^1].DisplayMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Persisted_document_has_versioned_minimal_schema()
    {
        var directory = CreateDataDirectory();
        try
        {
            var service = new ApplicationActivityLogService(directory);
            service.Record("parser.started", "Parser started", Now);

            using var document = JsonDocument.Parse(File.ReadAllText(service.ActivityLogPath));
            var root = document.RootElement;
            Assert.Equal(ApplicationActivityLogService.CurrentSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
            var entry = root.GetProperty("entries")[0];
            Assert.True(entry.TryGetProperty("timestamp", out _));
            Assert.True(entry.TryGetProperty("eventType", out _));
            Assert.True(entry.TryGetProperty("displayMessage", out _));
            Assert.Equal(3, entry.EnumerateObject().Count());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDataDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"coh-activity-{Guid.NewGuid():n}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}

using System.Text;
using System.Text.Json;
using CoHAnalytics.Services;
using CoHAnalytics.Services.Diagnostics;

namespace CoHAnalytics.Tests.Services.Diagnostics;

public sealed class DiagnosticLogServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Jsonl_records_are_compact_camel_case_null_omitting_and_utc()
    {
        WithDirectory(directory =>
        {
            using (var service = CreateService(directory))
            {
                service.Write(new ApplicationStartupFailedDiagnosticEvent
                {
                    ExceptionType = "System.InvalidOperationException",
                    HResult = -1
                });
            }

            var path = ApplicationDataPaths.GetStandardDiagnosticLogPath(directory);
            var lines = File.ReadAllLines(path);
            var line = Assert.Single(lines);
            Assert.DoesNotContain('\r', line);
            Assert.DoesNotContain('\n', line);
            Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble));

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("ApplicationStartupFailed", root.GetProperty("event").GetString());
            Assert.Equal(TimeSpan.Zero, root.GetProperty("ts").GetDateTimeOffset().Offset);
            Assert.False(root.TryGetProperty("SchemaVersion", out _));
            Assert.False(root.GetProperty("data").TryGetProperty("failureCode", out _));
            Assert.Equal(9, root.EnumerateObject().Count());
        });
    }

    [Fact]
    public void Each_instance_has_a_unique_application_run_id()
    {
        WithDirectory(directory =>
        {
            using var first = CreateService(directory);
            using var second = CreateService(directory);

            Assert.NotEqual(Guid.Empty, first.ApplicationRunId);
            Assert.NotEqual(first.ApplicationRunId, second.ApplicationRunId);
        });
    }

    [Fact]
    public void Sequence_is_monotonic_in_persisted_order()
    {
        WithDirectory(directory =>
        {
            using (var service = CreateService(directory))
            {
                service.Write(Started("one"));
                service.Write(Started("two"));
                service.Write(Started("three"));
            }

            Assert.Equal([1L, 2L, 3L], ReadRecords(directory).Select(GetSequence));
        });
    }

    [Fact]
    public void Concurrent_producers_write_valid_complete_records()
    {
        WithDirectory(directory =>
        {
            const int producerCount = 8;
            const int eventsPerProducer = 100;
            using (var service = CreateService(directory, new DiagnosticLogOptions
            {
                QueueCapacity = producerCount * eventsPerProducer,
                TimeProvider = new FixedTimeProvider(Now)
            }))
            {
                Parallel.For(0, producerCount, producer =>
                {
                    for (var index = 0; index < eventsPerProducer; index++)
                    {
                        service.Write(Started($"{producer}-{index}"));
                    }
                });
            }

            var records = ReadRecords(directory);
            Assert.Equal(producerCount * eventsPerProducer, records.Count);
            Assert.Equal(
                Enumerable.Range(1, records.Count).Select(value => (long)value),
                records.Select(GetSequence));
        });
    }

    [Fact]
    public void Overflow_rejects_newest_preserves_queue_and_emits_drop_marker()
    {
        WithDirectory(directory =>
        {
            using var writerEntered = new ManualResetEventSlim();
            using var releaseWriter = new ManualResetEventSlim();
            var writeCalls = 0;
            var options = new DiagnosticLogOptions
            {
                QueueCapacity = 2,
                MaximumBatchSize = 1,
                TimeProvider = new FixedTimeProvider(Now),
                BeforeWrite = () =>
                {
                    if (Interlocked.Increment(ref writeCalls) == 1)
                    {
                        writerEntered.Set();
                        releaseWriter.Wait();
                    }
                }
            };

            using (var service = CreateService(directory, options))
            {
                service.Write(Started("in-flight"));
                Assert.True(writerEntered.Wait(TimeSpan.FromSeconds(5)));
                service.Write(Started("queued-one"));
                service.Write(Started("queued-two"));
                service.Write(Started("rejected-newest"));
                releaseWriter.Set();
            }

            var records = ReadRecords(directory);
            var versions = records
                .Where(record => GetEvent(record) == "ApplicationRunStarted")
                .Select(record => record.GetProperty("data").GetProperty("applicationVersion").GetString()!)
                .ToArray();
            Assert.Equal(["in-flight", "queued-one", "queued-two"], versions);
            Assert.DoesNotContain("rejected-newest", versions);
            var marker = Assert.Single(records, record => GetEvent(record) == "DiagnosticRecordsDropped");
            Assert.Equal(1, marker.GetProperty("data").GetProperty("droppedLogCount").GetInt64());
        });
    }

    [Fact]
    public void Rotation_uses_unique_archives_and_preserves_valid_jsonl()
    {
        WithDirectory(directory =>
        {
            using (var service = CreateService(directory, new DiagnosticLogOptions
            {
                MaximumActiveFileBytes = 500,
                MaximumArchiveCount = 10,
                MaximumBatchSize = 1,
                TimeProvider = new FixedTimeProvider(Now)
            }))
            {
                for (var index = 0; index < 6; index++)
                {
                    service.Write(Started(new string((char)('a' + index), 220)));
                }
            }

            var logs = ApplicationDataPaths.GetLogsRoot(directory);
            var archives = Directory.GetFiles(logs, "coh-analytics-*.jsonl");
            Assert.True(archives.Length >= 2);
            Assert.Equal(archives.Length, archives.Select(Path.GetFileName).Distinct().Count());
            foreach (var path in archives.Append(ApplicationDataPaths.GetStandardDiagnosticLogPath(directory)))
            {
                foreach (var line in File.ReadLines(path))
                {
                    using var _ = JsonDocument.Parse(line);
                }
            }
        });
    }

    [Fact]
    public void Initialization_prunes_archives_by_count()
    {
        WithDirectory(directory =>
        {
            var logs = ApplicationDataPaths.GetLogsRoot(directory);
            Directory.CreateDirectory(logs);
            for (var index = 0; index < 7; index++)
            {
                var path = Path.Combine(logs, $"coh-analytics-20260902T14300{index}000Z.jsonl");
                File.WriteAllText(path, "{}\n");
                File.SetLastWriteTimeUtc(path, Now.AddMinutes(index).UtcDateTime);
            }

            using var service = CreateService(directory);

            Assert.Equal(4, Directory.GetFiles(logs, "coh-analytics-*.jsonl").Length);
        });
    }

    [Fact]
    public void Initialization_prunes_archives_by_age()
    {
        WithDirectory(directory =>
        {
            var logs = ApplicationDataPaths.GetLogsRoot(directory);
            Directory.CreateDirectory(logs);
            var oldPath = Path.Combine(logs, "coh-analytics-20260801T000000000Z.jsonl");
            var recentPath = Path.Combine(logs, "coh-analytics-20260901T000000000Z.jsonl");
            File.WriteAllText(oldPath, "truncated previous content");
            File.WriteAllText(recentPath, "{}\n");
            File.SetLastWriteTimeUtc(oldPath, Now.AddDays(-15).UtcDateTime);
            File.SetLastWriteTimeUtc(recentPath, Now.AddDays(-1).UtcDateTime);

            using var service = CreateService(directory);

            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(recentPath));
        });
    }

    [Fact]
    public void Clean_shutdown_drains_all_queued_events()
    {
        WithDirectory(directory =>
        {
            using (var service = CreateService(directory, new DiagnosticLogOptions
            {
                QueueCapacity = 1000,
                TimeProvider = new FixedTimeProvider(Now)
            }))
            {
                for (var index = 0; index < 500; index++)
                {
                    service.Write(Started(index.ToString()));
                }
            }

            Assert.Equal(500, ReadRecords(directory).Count);
        });
    }

    [Fact]
    public void Serialization_failure_drops_only_the_bad_event()
    {
        WithDirectory(directory =>
        {
            using var service = CreateService(directory, new DiagnosticLogOptions
            {
                TimeProvider = new FixedTimeProvider(Now),
                BeforeSerialize = diagnosticEvent =>
                {
                    if (diagnosticEvent is ApplicationRunStartedDiagnosticEvent { ApplicationVersion: "bad" })
                    {
                        throw new JsonException("Injected serialization failure.");
                    }
                }
            });
            service.Write(Started("bad"));
            service.Write(Started("good"));
            service.Dispose();

            var records = ReadRecords(directory);
            Assert.Contains(records, record =>
                record.GetProperty("data").TryGetProperty("applicationVersion", out var value)
                && value.GetString() == "good");
            Assert.DoesNotContain(records, record =>
                record.GetProperty("data").TryGetProperty("applicationVersion", out var value)
                && value.GetString() == "bad");
            Assert.Equal("serialization_failed", service.GetStatus().LastFailureCode);
            Assert.Equal(1, service.GetStatus().DroppedCount);
        });
    }

    [Fact]
    public void Write_failure_degrades_sink_and_future_writes_remain_no_throw()
    {
        WithDirectory(directory =>
        {
            using var service = CreateService(directory, new DiagnosticLogOptions
            {
                TimeProvider = new FixedTimeProvider(Now),
                BeforeWrite = () => throw new IOException("Injected write failure.")
            });
            service.Write(Started("fails"));
            WaitUntil(() => service.GetStatus().StreamState == DiagnosticLogStreamState.Degraded);

            var exception = Record.Exception(() => service.Write(Started("after-degradation")));

            Assert.Null(exception);
            Assert.Equal("sink_write_failed", service.GetStatus().LastFailureCode);
            Assert.True(service.GetStatus().DroppedCount >= 2);
        });
    }

    [Fact]
    public void Rotation_failure_degrades_sink_without_throwing_to_producer()
    {
        WithDirectory(directory =>
        {
            using var service = CreateService(directory, new DiagnosticLogOptions
            {
                MaximumActiveFileBytes = 500,
                MaximumBatchSize = 1,
                TimeProvider = new FixedTimeProvider(Now),
                BeforeRotation = () => throw new IOException("Injected rotation failure.")
            });
            service.Write(Started(new string('a', 220)));
            WaitUntil(() => service.GetStatus().WrittenCount == 1);

            var exception = Record.Exception(() => service.Write(Started(new string('b', 220))));
            Assert.Null(exception);
            WaitUntil(() => service.GetStatus().StreamState == DiagnosticLogStreamState.Degraded);

            Assert.Equal("sink_rotation_failed", service.GetStatus().LastFailureCode);
        });
    }

    [Fact]
    public void Initialization_failure_is_isolated_and_write_is_no_throw()
    {
        WithDirectory(directory =>
        {
            File.WriteAllText(ApplicationDataPaths.GetLogsRoot(directory), "blocks directory creation");

            using var service = CreateService(directory);
            var exception = Record.Exception(() => service.Write(Started("ignored")));

            Assert.Null(exception);
            Assert.Equal(DiagnosticLogStreamState.Degraded, service.GetStatus().StreamState);
            Assert.Equal("sink_initialization_failed", service.GetStatus().LastFailureCode);
        });
    }

    [Fact]
    public void Malformed_existing_active_file_does_not_block_startup()
    {
        WithDirectory(directory =>
        {
            var path = ApplicationDataPaths.GetStandardDiagnosticLogPath(directory);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{truncated");

            using var service = CreateService(directory);
            service.Write(new ApplicationStartupCompletedDiagnosticEvent());
            service.Dispose();

            Assert.Equal(DiagnosticLogStreamState.Stopped, service.GetStatus().StreamState);
            using var record = JsonDocument.Parse(File.ReadAllLines(path)[^1]);
            Assert.Equal("ApplicationStartupCompleted", record.RootElement.GetProperty("event").GetString());
        });
    }

    [Fact]
    public void Diagnostic_logging_does_not_change_application_activity_history()
    {
        WithDirectory(directory =>
        {
            var activityLog = new ApplicationActivityLogService(directory);
            activityLog.Record("application.status", "Existing dashboard activity", Now);
            var originalDocument = File.ReadAllText(activityLog.ActivityLogPath);

            using (var diagnosticLog = CreateService(directory))
            {
                diagnosticLog.Write(new ApplicationStartupCompletedDiagnosticEvent());
            }

            var activity = Assert.Single(activityLog.Entries);
            Assert.Equal("Existing dashboard activity", activity.DisplayMessage);
            Assert.Equal(originalDocument, File.ReadAllText(activityLog.ActivityLogPath));
        });
    }

    private static DiagnosticLogService CreateService(string directory, DiagnosticLogOptions? options = null) =>
        new(directory, options ?? new DiagnosticLogOptions { TimeProvider = new FixedTimeProvider(Now) });

    private static ApplicationRunStartedDiagnosticEvent Started(string version) => new()
    {
        ApplicationVersion = version,
        ProcessId = 123
    };

    private static List<JsonElement> ReadRecords(string directory) =>
        File.ReadLines(ApplicationDataPaths.GetStandardDiagnosticLogPath(directory))
            .Where(line => line.StartsWith('{'))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();

    private static long GetSequence(JsonElement record) => record.GetProperty("sequence").GetInt64();

    private static string? GetEvent(JsonElement record) => record.GetProperty("event").GetString();

    private static void WaitUntil(Func<bool> condition)
    {
        Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5)));
    }

    private static void WithDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"coh-diagnostics-{Guid.NewGuid():n}");
        Directory.CreateDirectory(directory);
        try
        {
            action(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoHAnalytics.Services;
using CoHAnalytics.Services.Diagnostics;

namespace CoHAnalytics.Tests.Services.Diagnostics;

public sealed class DiagnosticsReportServiceTests
{
    [Fact]
    public void CreateReport_packages_summary_and_sanitized_logs_without_unsafe_files()
    {
        WithTempRoots((dataRoot, outputRoot) =>
        {
            var logsRoot = ApplicationDataPaths.GetLogsRoot(dataRoot);
            Directory.CreateDirectory(logsRoot);
            File.WriteAllText(
                Path.Combine(dataRoot, "settings.json"),
                """{"homecomingInstallPath":"C:\\Users\\SecretUser\\Games\\Homecoming"}""");
            File.WriteAllText(Path.Combine(dataRoot, "activity-history.json"), """{"schemaVersion":1,"entries":[]}""");

            var activePath = Path.Combine(logsRoot, "coh-analytics.jsonl");
            var archivePath = Path.Combine(logsRoot, "coh-analytics-20260907T010203000Z.jsonl");
            var originalActive = """
                {"schemaVersion":1,"event":"Parser.IdentityEvidenceClassified","data":{"candidateCharacterName":"SecretHero","accountStableId":"abc"}}
                {"schemaVersion":1,"event":"ApplicationRunStarted","data":{"applicationVersion":"0.1-beta.1","processId":12}}
                not-json
                """.Replace("\r\n", "\n");
            if (!originalActive.EndsWith('\n'))
            {
                originalActive += "\n";
            }

            var originalArchive = """
                {"schemaVersion":1,"event":"GameplaySession.WelcomeProcessed","data":{"candidateCharacterName":"AnotherHero","result":"Accepted"}}
                """.Replace("\r\n", "\n");
            if (!originalArchive.EndsWith('\n'))
            {
                originalArchive += "\n";
            }

            File.WriteAllText(activePath, originalActive);
            File.WriteAllText(archivePath, originalArchive);

            // Hold an exclusive-writer share mode matching RollingJsonlFileSink while exporting.
            using var liveWriter = new FileStream(
                activePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.None);
            var originalActiveHash = ComputeSha256Shared(activePath);
            var originalArchiveHash = SHA256.HashData(File.ReadAllBytes(archivePath));

            var service = CreateService(dataRoot, new StubDiagnosticLog());
            var zipPath = Path.Combine(outputRoot, "report.zip");

            var result = service.CreateReport(zipPath);

            Assert.True(result.Succeeded, result.FailureMessage);
            Assert.True(File.Exists(zipPath));
            Assert.Equal(originalActiveHash, ComputeSha256Shared(activePath));
            Assert.Equal(originalArchiveHash, SHA256.HashData(File.ReadAllBytes(archivePath)));

            using var archive = ZipFile.OpenRead(zipPath);
            Assert.NotNull(archive.GetEntry("summary.json"));
            Assert.NotNull(archive.GetEntry("Logs/coh-analytics.jsonl"));
            Assert.NotNull(archive.GetEntry("Logs/coh-analytics-20260907T010203000Z.jsonl"));
            Assert.Null(archive.GetEntry("settings.json"));
            Assert.Null(archive.GetEntry("activity-history.json"));
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("Observations", StringComparison.OrdinalIgnoreCase));

            var summaryJson = ReadEntryText(archive, "summary.json");
            using var summary = JsonDocument.Parse(summaryJson);
            Assert.Equal(1, summary.RootElement.GetProperty("reportSchemaVersion").GetInt32());
            Assert.Equal("0.1-beta.1", summary.RootElement.GetProperty("applicationVersion").GetString());
            Assert.Equal(2, summary.RootElement.GetProperty("runtimeClientCount").GetInt32());
            Assert.Equal(3, summary.RootElement.GetProperty("monitoringContextCount").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("parserWorkerCount").GetInt32());
            Assert.Equal("item-ref-3.1.0", summary.RootElement.GetProperty("catalogVersion").GetString());
            Assert.False(summaryJson.Contains("SecretUser", StringComparison.OrdinalIgnoreCase));
            Assert.False(summaryJson.Contains("candidateCharacterName", StringComparison.OrdinalIgnoreCase));
            Assert.True(summary.RootElement.TryGetProperty("exportWarnings", out var warnings));
            Assert.Contains(
                warnings.EnumerateArray().Select(item => item.GetString() ?? string.Empty),
                warning => warning.Contains("omitted_malformed_jsonl_line", StringComparison.Ordinal));

            var exportedActive = ReadEntryText(archive, "Logs/coh-analytics.jsonl");
            Assert.Contains("[REDACTED]", exportedActive, StringComparison.Ordinal);
            Assert.DoesNotContain("SecretHero", exportedActive, StringComparison.Ordinal);
            Assert.Contains("ApplicationRunStarted", exportedActive, StringComparison.Ordinal);

            var exportedArchive = ReadEntryText(archive, "Logs/coh-analytics-20260907T010203000Z.jsonl");
            Assert.Contains("[REDACTED]", exportedArchive, StringComparison.Ordinal);
            Assert.DoesNotContain("AnotherHero", exportedArchive, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CreateReport_succeeds_when_only_active_log_exists()
    {
        WithTempRoots((dataRoot, outputRoot) =>
        {
            var logsRoot = ApplicationDataPaths.GetLogsRoot(dataRoot);
            Directory.CreateDirectory(logsRoot);
            File.WriteAllText(
                Path.Combine(logsRoot, "coh-analytics.jsonl"),
                """{"schemaVersion":1,"event":"ApplicationStartupCompleted","data":{}}""" + "\n");

            var diagnosticLog = new StubDiagnosticLog();
            var service = CreateService(dataRoot, diagnosticLog);
            var zipPath = Path.Combine(outputRoot, "report-active-only.zip");

            var result = service.CreateReport(zipPath);

            Assert.True(result.Succeeded, result.FailureMessage);
            using var archive = ZipFile.OpenRead(zipPath);
            Assert.NotNull(archive.GetEntry("summary.json"));
            Assert.NotNull(archive.GetEntry("Logs/coh-analytics.jsonl"));
            Assert.DoesNotContain(
                archive.Entries,
                entry => entry.FullName.StartsWith("Logs/coh-analytics-", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void CreateReport_succeeds_when_no_log_files_exist()
    {
        WithTempRoots((dataRoot, outputRoot) =>
        {
            var diagnosticLog = new StubDiagnosticLog();
            var service = CreateService(dataRoot, diagnosticLog);
            var zipPath = Path.Combine(outputRoot, "report-empty-logs.zip");

            var result = service.CreateReport(zipPath);

            Assert.True(result.Succeeded, result.FailureMessage);
            using var archive = ZipFile.OpenRead(zipPath);
            Assert.NotNull(archive.GetEntry("summary.json"));
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("Logs/", StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData("""{"data":{"candidateCharacterName":"Hero"}}""", "Hero")]
    [InlineData("""{"data":{"characterName":"HeroTwo"}}""", "HeroTwo")]
    public void TrySanitizeJsonlLine_redacts_character_name_fields(string line, string sensitive)
    {
        Assert.True(DiagnosticsReportService.TrySanitizeJsonlLine(line, out var sanitized, out var warning));
        Assert.Null(warning);
        Assert.Contains(DiagnosticsReportService.RedactedPlaceholder, sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitive, sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void TrySanitizeJsonlLine_omits_malformed_lines()
    {
        Assert.False(DiagnosticsReportService.TrySanitizeJsonlLine("{not-json", out _, out var warning));
        Assert.Equal("omitted_malformed_jsonl_line", warning);
    }

    [Fact]
    public void Default_file_name_uses_local_timestamp_pattern()
    {
        var name = DiagnosticsReportService.CreateDefaultFileName(
            new DateTimeOffset(2026, 9, 7, 16, 2, 5, TimeSpan.FromHours(-4)));
        Assert.Equal("CoH-Analytics-Diagnostics-2026-09-07-160205.zip", name);
    }

    private static DiagnosticsReportService CreateService(string dataRoot, IDiagnosticLog diagnosticLog) =>
        new(
            dataRoot,
            diagnosticLog,
            applicationVersionProvider: () => "0.1-beta.1",
            runtimeClientCountProvider: () => 2,
            monitoringContextCountProvider: () => 3,
            parserSnapshotProvider: () => new DiagnosticsReportService.ParserReportSnapshot(
                1,
                true,
                new Dictionary<string, int>(StringComparer.Ordinal) { ["Running"] = 1 }),
            catalogVersionProvider: () => "item-ref-3.1.0");

    private static string ReadEntryText(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] ComputeSha256Shared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        return SHA256.HashData(stream);
    }

    private static void WithTempRoots(Action<string, string> action)
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "coh-diag-report-" + Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(Path.GetTempPath(), "coh-diag-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(outputRoot);
        try
        {
            action(dataRoot, outputRoot);
        }
        finally
        {
            try
            {
                Directory.Delete(dataRoot, recursive: true);
            }
            catch
            {
            }

            try
            {
                Directory.Delete(outputRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class StubDiagnosticLog : IDiagnosticLog
    {
        public Guid ApplicationRunId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

        public bool IsEnabled(DiagnosticChannel channel, DiagnosticCategory category) => false;

        public void Write(DiagnosticEvent diagnosticEvent)
        {
        }

        public DiagnosticLogStatus GetStatus() =>
            new()
            {
                StreamState = DiagnosticLogStreamState.Active,
                ActivePath = @"C:\Users\SecretUser\AppData\Local\CoH Analytics\Logs\coh-analytics.jsonl",
                ApplicationRunId = ApplicationRunId,
                QueueDepth = 0,
                PeakQueueDepth = 0,
                WrittenCount = 0,
                DroppedCount = 0
            };
    }
}

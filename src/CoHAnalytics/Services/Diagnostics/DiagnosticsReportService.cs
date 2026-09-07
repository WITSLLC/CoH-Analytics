using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoHAnalytics.Services.Diagnostics;

/// <summary>
/// Packages a sanitized diagnostics ZIP from existing LocalAppData JSONL logs plus a curated summary.
/// Does not pause logging, modify live logs, or include settings/activity/observations/chat files.
/// </summary>
public sealed class DiagnosticsReportService : IDiagnosticsReportService
{
    public const int ReportSchemaVersion = 1;
    public const string SummaryFileName = "summary.json";
    public const string LogsDirectoryName = "Logs";
    public const string RedactedPlaceholder = "[REDACTED]";

    private static readonly JsonSerializerOptions SummarySerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> CharacterNamePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "candidateCharacterName",
        "characterName"
    };

    private readonly string _dataDirectory;
    private readonly IDiagnosticLog _diagnosticLog;
    private readonly Func<string> _applicationVersionProvider;
    private readonly Func<int> _runtimeClientCountProvider;
    private readonly Func<int> _monitoringContextCountProvider;
    private readonly Func<ParserReportSnapshot> _parserSnapshotProvider;
    private readonly Func<string?> _catalogVersionProvider;
    private readonly TimeProvider _timeProvider;

    public DiagnosticsReportService(
        string dataDirectory,
        IDiagnosticLog diagnosticLog,
        Func<string> applicationVersionProvider,
        Func<int> runtimeClientCountProvider,
        Func<int> monitoringContextCountProvider,
        Func<ParserReportSnapshot> parserSnapshotProvider,
        Func<string?> catalogVersionProvider,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _dataDirectory = dataDirectory;
        _diagnosticLog = diagnosticLog ?? throw new ArgumentNullException(nameof(diagnosticLog));
        _applicationVersionProvider = applicationVersionProvider
            ?? throw new ArgumentNullException(nameof(applicationVersionProvider));
        _runtimeClientCountProvider = runtimeClientCountProvider
            ?? throw new ArgumentNullException(nameof(runtimeClientCountProvider));
        _monitoringContextCountProvider = monitoringContextCountProvider
            ?? throw new ArgumentNullException(nameof(monitoringContextCountProvider));
        _parserSnapshotProvider = parserSnapshotProvider
            ?? throw new ArgumentNullException(nameof(parserSnapshotProvider));
        _catalogVersionProvider = catalogVersionProvider
            ?? throw new ArgumentNullException(nameof(catalogVersionProvider));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static string CreateDefaultFileName(DateTimeOffset localNow) =>
        $"CoH-Analytics-Diagnostics-{localNow:yyyy-MM-dd-HHmmss}.zip";

    public DiagnosticsReportCreationResult CreateReport(string destinationZipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationZipPath);

        try
        {
            var destinationDirectory = Path.GetDirectoryName(destinationZipPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            var warnings = new List<string>();
            var logsRoot = ApplicationDataPaths.GetLogsRoot(_dataDirectory);
            var sourceFiles = EnumerateSourceLogFiles(logsRoot).ToArray();

            var tempPath = destinationZipPath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            using (var zipStream = new FileStream(
                       tempPath,
                       FileMode.Create,
                       FileAccess.ReadWrite,
                       FileShare.None))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var sourceFile in sourceFiles)
                {
                    WriteSanitizedLogEntry(archive, sourceFile, warnings);
                }

                var summary = BuildSummary(sourceFiles.Select(file => file.Name).ToArray(), warnings);
                var summaryEntry = archive.CreateEntry(SummaryFileName, CompressionLevel.Optimal);
                using var summaryStream = summaryEntry.Open();
                using var writer = new StreamWriter(summaryStream, new UTF8Encoding(false));
                writer.Write(JsonSerializer.Serialize(summary, SummarySerializerOptions));
            }

            File.Move(tempPath, destinationZipPath, overwrite: true);
            return DiagnosticsReportCreationResult.Success(destinationZipPath);
        }
        catch (Exception exception)
        {
            try
            {
                var tempPath = destinationZipPath + ".tmp";
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }

            try
            {
                if (File.Exists(destinationZipPath))
                {
                    File.Delete(destinationZipPath);
                }
            }
            catch
            {
            }

            _diagnosticLog.Write(new DiagnosticsReportExportFailedDiagnosticEvent
            {
                FailureCode = "diagnostics_report_export_failed",
                ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                HResult = exception.HResult
            });

            return DiagnosticsReportCreationResult.Failure(
                "diagnostics_report_export_failed",
                "CoH Analytics could not create the diagnostics report.");
        }
    }

    private DiagnosticsReportSummary BuildSummary(IReadOnlyList<string> includedLogFiles, IReadOnlyList<string> warnings)
    {
        var status = _diagnosticLog.GetStatus();
        var parser = SafeInvoke(
            _parserSnapshotProvider,
            new ParserReportSnapshot(0, false, new Dictionary<string, int>(StringComparer.Ordinal)));
        var generatedAt = _timeProvider.GetUtcNow().ToUniversalTime();

        return new DiagnosticsReportSummary
        {
            ReportSchemaVersion = ReportSchemaVersion,
            GeneratedAt = generatedAt,
            ApplicationVersion = SafeInvoke(_applicationVersionProvider, "unknown"),
            ApplicationRunId = status.ApplicationRunId,
            DiagnosticLogStatus = new DiagnosticsReportLogStatusSummary
            {
                StreamState = status.StreamState.ToString(),
                QueueDepth = status.QueueDepth,
                PeakQueueDepth = status.PeakQueueDepth,
                WrittenCount = status.WrittenCount,
                DroppedCount = status.DroppedCount,
                LastSuccessfulWriteAtUtc = status.LastSuccessfulWriteAtUtc,
                LastFailureCode = status.LastFailureCode,
                LastFailureType = status.LastFailureType,
                LastFailureHResult = status.LastFailureHResult,
                LastFailureAtUtc = status.LastFailureAtUtc
            },
            RuntimeClientCount = SafeInvoke(_runtimeClientCountProvider, 0),
            MonitoringContextCount = SafeInvoke(_monitoringContextCountProvider, 0),
            ParserIsRunning = parser.IsRunning,
            ParserWorkerCount = parser.WorkerCount,
            ParserWorkerStateCounts = parser.StateCounts,
            CatalogVersion = SafeInvoke(_catalogVersionProvider, null),
            IncludedLogFiles = includedLogFiles,
            ExportWarnings = warnings.Count == 0 ? null : warnings
        };
    }

    private static IEnumerable<FileInfo> EnumerateSourceLogFiles(string logsRoot)
    {
        if (!Directory.Exists(logsRoot))
        {
            yield break;
        }

        var active = new FileInfo(Path.Combine(logsRoot, RollingJsonlFileSink.ActiveFileName));
        if (active.Exists)
        {
            yield return active;
        }

        foreach (var archive in Directory
                     .EnumerateFiles(logsRoot, RollingJsonlFileSink.ArchiveSearchPattern, SearchOption.TopDirectoryOnly)
                     .Select(path => new FileInfo(path))
                     .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase))
        {
            yield return archive;
        }
    }

    private static void WriteSanitizedLogEntry(
        ZipArchive archive,
        FileInfo sourceFile,
        List<string> warnings)
    {
        var entryName = $"{LogsDirectoryName}/{sourceFile.Name}";
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));

        using var sourceStream = new FileStream(
            sourceFile.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using var reader = new StreamReader(sourceStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        string? line;
        var lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!TrySanitizeJsonlLine(line, out var sanitized, out var warning))
            {
                warnings.Add($"{sourceFile.Name}:{lineNumber}: {warning}");
                continue;
            }

            writer.Write(sanitized);
            writer.Write('\n');
        }
    }

    internal static bool TrySanitizeJsonlLine(string line, out string sanitized, out string? warning)
    {
        warning = null;
        sanitized = line;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            warning = "omitted_malformed_jsonl_line";
            return false;
        }

        if (root is not JsonObject)
        {
            warning = "omitted_non_object_jsonl_line";
            return false;
        }

        RedactCharacterNameFields(root);
        sanitized = root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        });
        return true;
    }

    private static void RedactCharacterNameFields(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (CharacterNamePropertyNames.Contains(property.Key))
                {
                    obj[property.Key] = RedactedPlaceholder;
                    continue;
                }

                if (property.Value is not null)
                {
                    RedactCharacterNameFields(property.Value);
                }
            }

            return;
        }

        if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null)
                {
                    RedactCharacterNameFields(child);
                }
            }
        }
    }

    private static T SafeInvoke<T>(Func<T> provider, T fallback)
    {
        try
        {
            return provider();
        }
        catch
        {
            return fallback;
        }
    }

    public sealed record ParserReportSnapshot(
        int WorkerCount,
        bool IsRunning,
        IReadOnlyDictionary<string, int> StateCounts);
}

internal sealed class DiagnosticsReportSummary
{
    public required int ReportSchemaVersion { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required string ApplicationVersion { get; init; }

    public required Guid ApplicationRunId { get; init; }

    public required DiagnosticsReportLogStatusSummary DiagnosticLogStatus { get; init; }

    public required int RuntimeClientCount { get; init; }

    public required int MonitoringContextCount { get; init; }

    public required bool ParserIsRunning { get; init; }

    public required int ParserWorkerCount { get; init; }

    public required IReadOnlyDictionary<string, int> ParserWorkerStateCounts { get; init; }

    public string? CatalogVersion { get; init; }

    public required IReadOnlyList<string> IncludedLogFiles { get; init; }

    public IReadOnlyList<string>? ExportWarnings { get; init; }
}

internal sealed class DiagnosticsReportLogStatusSummary
{
    public required string StreamState { get; init; }

    public required int QueueDepth { get; init; }

    public required int PeakQueueDepth { get; init; }

    public required long WrittenCount { get; init; }

    public required long DroppedCount { get; init; }

    public DateTimeOffset? LastSuccessfulWriteAtUtc { get; init; }

    public string? LastFailureCode { get; init; }

    public string? LastFailureType { get; init; }

    public int? LastFailureHResult { get; init; }

    public DateTimeOffset? LastFailureAtUtc { get; init; }
}

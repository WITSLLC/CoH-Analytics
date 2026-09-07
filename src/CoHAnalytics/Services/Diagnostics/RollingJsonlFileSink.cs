using System.Text;

namespace CoHAnalytics.Services.Diagnostics;

internal sealed class RollingJsonlFileSink : IDisposable
{
    internal const string ActiveFileName = "coh-analytics.jsonl";
    internal const string ArchiveSearchPattern = "coh-analytics-*.jsonl";

    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    private readonly string _logsDirectory;
    private readonly DiagnosticLogOptions _options;
    private StreamWriter? _writer;
    private long _activeBytes;
    private bool _disposed;

    public RollingJsonlFileSink(string logsDirectory, DiagnosticLogOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        ArgumentNullException.ThrowIfNull(options);

        _logsDirectory = logsDirectory;
        _options = options;
        ActivePath = Path.Combine(_logsDirectory, ActiveFileName);

        try
        {
            Directory.CreateDirectory(_logsDirectory);
            PruneArchives(_options.TimeProvider.GetUtcNow());
            OpenActiveFile();
        }
        catch (Exception exception)
        {
            DisposeNoThrow();
            throw new DiagnosticLogSinkException("sink_initialization_failed", exception);
        }
    }

    public string ActivePath { get; }

    public void WriteBatch(IReadOnlyList<string> lines, DateTimeOffset observedAt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (lines.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var line in lines)
            {
                var encodedLength = Utf8WithoutBom.GetByteCount(line) + 1L;
                if (_activeBytes > 0 && _activeBytes + encodedLength > _options.MaximumActiveFileBytes)
                {
                    Rotate(observedAt);
                }

                _options.BeforeWrite?.Invoke();
                _writer!.Write(line);
                _writer.Write('\n');
                _activeBytes += encodedLength;
            }

            _writer!.Flush();
        }
        catch (DiagnosticLogSinkException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DiagnosticLogSinkException("sink_write_failed", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeNoThrow();
    }

    private void Rotate(DateTimeOffset observedAt)
    {
        try
        {
            _options.BeforeRotation?.Invoke();
            _writer!.Flush();
            _writer.Dispose();
            _writer = null;

            var archivePath = ResolveArchivePath(observedAt);
            File.Move(ActivePath, archivePath, overwrite: false);
            OpenActiveFile();
            PruneArchives(observedAt);
        }
        catch (Exception exception)
        {
            throw new DiagnosticLogSinkException("sink_rotation_failed", exception);
        }
    }

    private string ResolveArchivePath(DateTimeOffset observedAt)
    {
        var stem = $"coh-analytics-{observedAt.UtcDateTime:yyyyMMdd'T'HHmmssfff'Z'}";
        var candidate = Path.Combine(_logsDirectory, stem + ".jsonl");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var suffix = 1; suffix <= 9999; suffix++)
        {
            candidate = Path.Combine(_logsDirectory, $"{stem}-{suffix:D4}.jsonl");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("No available diagnostic archive name could be allocated.");
    }

    private void OpenActiveFile()
    {
        var stream = new FileStream(
            ActivePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan);
        _activeBytes = stream.Length;
        if (_activeBytes > 0)
        {
            stream.Seek(-1, SeekOrigin.End);
            var finalByte = stream.ReadByte();
            stream.Seek(0, SeekOrigin.End);
            if (finalByte != '\n')
            {
                stream.WriteByte((byte)'\n');
                _activeBytes++;
            }
        }

        _writer = new StreamWriter(stream, Utf8WithoutBom, bufferSize: 16 * 1024, leaveOpen: false)
        {
            NewLine = "\n"
        };
    }

    private void PruneArchives(DateTimeOffset now)
    {
        var cutoff = now.UtcDateTime - _options.MaximumArchiveAge;
        var archives = Directory
            .EnumerateFiles(_logsDirectory, ArchiveSearchPattern, SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (var archive in archives.Where(file => file.LastWriteTimeUtc < cutoff))
        {
            DeleteArchiveNoThrow(archive.FullName);
        }

        archives = Directory
            .EnumerateFiles(_logsDirectory, ArchiveSearchPattern, SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (var archive in archives.Skip(_options.MaximumArchiveCount))
        {
            DeleteArchiveNoThrow(archive.FullName);
        }
    }

    private static void DeleteArchiveNoThrow(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private void DisposeNoThrow()
    {
        try
        {
            _writer?.Flush();
        }
        catch
        {
        }

        try
        {
            _writer?.Dispose();
        }
        catch
        {
        }

        _writer = null;
    }
}

internal sealed class DiagnosticLogSinkException(string failureCode, Exception innerException)
    : IOException(innerException.Message, innerException)
{
    public string FailureCode { get; } = failureCode;
}

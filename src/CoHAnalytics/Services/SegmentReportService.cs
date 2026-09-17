using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public interface ISegmentReportService
{
    string? GenerateAndOpen(GameplaySessionId sessionId, int segmentOrdinal);
}

public interface IReportBrowserLauncher
{
    void Open(string path);
}

public sealed class WindowsReportBrowserLauncher : IReportBrowserLauncher
{
    public static ProcessStartInfo Prepare(string path) => new()
    {
        FileName = Path.GetFullPath(path),
        UseShellExecute = true
    };

    public void Open(string path) => Process.Start(Prepare(path))?.Dispose();
}

/// <summary>Historical read, presentation, file output and shell launch; no analytical work.</summary>
public sealed class SegmentReportService(
    IHistoricalSegmentReader reader,
    IReportBrowserLauncher launcher,
    string? dataDirectory = null) : ISegmentReportService
{
    public string? GenerateAndOpen(GameplaySessionId sessionId, int segmentOrdinal)
    {
        HistoricalLoadResult loaded;
        try
        {
            loaded = reader.TryLoad(sessionId, segmentOrdinal);
        }
        catch (Exception)
        {
            return "The Segment could not be loaded for its report. Please try again.";
        }

        if (!loaded.HasAuthoritativeAggregates || loaded.Segment is not { } segment)
            return "The Segment has no readable authoritative analytics for a report.";

        string html;
        try { html = new HtmlReportRenderer().Render(segment); }
        catch (Exception) { return "The Segment report could not be rendered."; }

        string path;
        try
        {
            var directory = ApplicationDataPaths.GetReportsDirectory(dataDirectory);
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, GetFileName(segment.Header));
            File.WriteAllText(path, html, new UTF8Encoding(false));
        }
        catch (Exception)
        {
            return "The report could not be saved. Check available disk space and application-data folder permissions.";
        }

        try { launcher.Open(path); }
        catch (Exception) { return "The report was saved, but could not be opened. Check your default browser settings and try again."; }
        return null;
    }

    public static string GetFileName(HistoricalSegmentHeader header)
    {
        // A full identity digest prevents collisions after sanitizing/truncating display names,
        // and avoids embedding identifiers with unknown provenance in a public-facing filename.
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(header.SegmentId))).ToLowerInvariant();
        var name = header.CharacterDisplayNameAtCapture ?? "character";
        var safe = new string(name.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray());
        safe = safe[..Math.Min(safe.Length, 48)].TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(safe)) safe = "character";
        return $"{safe}_{header.CaptureStartUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}_{identity}.html";
    }
}

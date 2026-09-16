using System.IO.Compression;
using System.Text;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Gzip JSON codec for <c>spine.bin</c>. No raw log text.</summary>
public static class SegmentSpineCodec
{
    internal const int MaxDecompressedBytes = 8 * 1024 * 1024;

    public static byte[] Encode(IReadOnlyList<PersistedSpineEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var document = new SpineDocument
        {
            SchemaVersion = SpineSchemaVersion.Current,
            Events = events.ToArray()
        };
        var json = SegmentJson.Serialize(document);
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var payload = Encoding.UTF8.GetBytes(json);
            gzip.Write(payload, 0, payload.Length);
        }

        return buffer.ToArray();
    }

    public static IReadOnlyList<PersistedSpineEvent> Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var limited = new MemoryStream();
        var buffer = new byte[8192];
        var total = 0;
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxDecompressedBytes)
            {
                throw new InvalidDataException("Spine payload exceeds the decompression bound.");
            }

            limited.Write(buffer, 0, read);
        }

        var json = Encoding.UTF8.GetString(limited.ToArray());
        var document = SegmentJson.Deserialize<SpineDocument>(json);
        if (document.SchemaVersion != SpineSchemaVersion.Current)
        {
            throw new InvalidDataException(
                $"Spine schema version {document.SchemaVersion} is not supported.");
        }

        if (document.Events.Count > SegmentSpineLimits.MaxRetainedLogicalEvents)
        {
            throw new InvalidDataException("Spine event count exceeds the retention bound.");
        }

        return document.Events;
    }

    private sealed class SpineDocument
    {
        public int SchemaVersion { get; init; }

        public IReadOnlyList<PersistedSpineEvent> Events { get; init; } = [];
    }
}

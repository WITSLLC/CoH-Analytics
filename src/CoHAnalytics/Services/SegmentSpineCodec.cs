using System.IO.Compression;
using System.Text;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Gzip JSON codec for <c>spine.bin</c>. No raw log text.</summary>
public static class SegmentSpineCodec
{
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
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var json = reader.ReadToEnd();
        var document = SegmentJson.Deserialize<SpineDocument>(json);
        if (document.SchemaVersion != SpineSchemaVersion.Current)
        {
            throw new InvalidDataException(
                $"Spine schema version {document.SchemaVersion} is not supported.");
        }

        return document.Events;
    }

    private sealed class SpineDocument
    {
        public int SchemaVersion { get; init; }

        public IReadOnlyList<PersistedSpineEvent> Events { get; init; } = [];
    }
}

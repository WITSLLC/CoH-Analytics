using System.IO;
using System.Text;

namespace CoHAnalytics.HomecomingBinary;

internal static class HomecomingParse7HeaderReader
{
    private const ushort SupportedBinaryVersion = 6;
    private static readonly byte[] CrypticSignature = "CrypticS"u8.ToArray();
    private static readonly byte[] Parse7Signature = "Parse7"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static HomecomingParse7StringPool Read(BinaryReader reader, string domain)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        RequireSignature(reader, CrypticSignature, $"{domain} Cryptic signature");
        _ = ReadUInt32(reader, $"{domain} schema hash");
        if (ReadUInt16(reader, $"{domain} binary version") != SupportedBinaryVersion)
        {
            throw new InvalidDataException($"{domain} binary version is not supported.");
        }

        RequireSignature(reader, Parse7Signature, $"{domain} Parse7 signature");
        var byteLength = ReadUInt32(reader, $"{domain} string-pool length");
        if (byteLength > int.MaxValue)
        {
            throw new InvalidDataException($"{domain} string pool is too large.");
        }

        var bytes = ReadExactly(reader, checked((int)byteLength), $"{domain} string pool");
        var paddingLength = (4 - (checked((int)byteLength) % 4)) % 4;
        var padding = ReadExactly(reader, paddingLength, $"{domain} string-pool padding");
        if (padding.Any(value => value != 0))
        {
            throw new InvalidDataException($"{domain} string-pool padding is invalid.");
        }

        return new HomecomingParse7StringPool(bytes, StrictUtf8, domain);
    }

    private static void RequireSignature(BinaryReader reader, byte[] expected, string field)
    {
        if (!ReadExactly(reader, expected.Length, field).AsSpan().SequenceEqual(expected))
        {
            throw new InvalidDataException($"{field} is invalid.");
        }
    }

    private static ushort ReadUInt16(BinaryReader reader, string field)
    {
        EnsureAvailable(reader, sizeof(ushort), field);
        return reader.ReadUInt16();
    }

    private static uint ReadUInt32(BinaryReader reader, string field)
    {
        EnsureAvailable(reader, sizeof(uint), field);
        return reader.ReadUInt32();
    }

    private static byte[] ReadExactly(BinaryReader reader, int length, string field)
    {
        EnsureAvailable(reader, length, field);
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException($"Parse7 data is truncated while reading {field}.");
        }

        return bytes;
    }

    private static void EnsureAvailable(BinaryReader reader, long length, string field)
    {
        if (length < 0 || reader.BaseStream.Position > reader.BaseStream.Length - length)
        {
            throw new EndOfStreamException($"Parse7 data is truncated while reading {field}.");
        }
    }
}

internal sealed class HomecomingParse7StringPool(
    byte[] bytes,
    UTF8Encoding encoding,
    string domain)
{
    private readonly IReadOnlyDictionary<uint, string> _strings =
        BuildStringIndex(bytes, encoding, domain);

    internal string Resolve(uint offset, string field)
    {
        if (!_strings.TryGetValue(offset, out var value))
        {
            throw new InvalidDataException(
                $"{domain} {field} refers to invalid string-pool offset {offset}.");
        }

        return value;
    }

    private static IReadOnlyDictionary<uint, string> BuildStringIndex(
        byte[] bytes,
        UTF8Encoding encoding,
        string domain)
    {
        var strings = new Dictionary<uint, string>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            var terminator = Array.IndexOf(bytes, (byte)0, offset);
            if (terminator < 0)
            {
                throw new InvalidDataException($"{domain} string pool is not null terminated.");
            }

            strings.Add(
                checked((uint)offset),
                encoding.GetString(bytes, offset, terminator - offset));
            offset = terminator + 1;
        }

        return strings;
    }
}

using System.IO;
using System.IO.Compression;
using System.Text;

namespace CoHAnalytics.Homecoming;

public static class HomecomingPiggMemberReader
{
    private const uint ArchiveMagic = 0x00000123;
    private const uint EntryMagic = 0x00003456;
    private const uint StringTableMagic = 0x00006789;
    private const ushort SupportedVersion = 2;
    private const ushort ArchiveHeaderLength = 16;
    private const ushort MinimumEntryHeaderLength = 48;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] ReadMember(string archivePath, string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        try
        {
            return ReadMember(stream, memberName);
        }
        catch (HomecomingPiggException)
        {
            throw;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or DecoderFallbackException)
        {
            throw new HomecomingPiggException(
                $"Homecoming PIGG archive '{archivePath}' is malformed or truncated: {exception.Message}",
                exception);
        }
    }

    public static byte[] ReadMember(Stream stream, string memberName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("The PIGG source stream must be readable and seekable.", nameof(stream));
        }

        using var reader = new BinaryReader(stream, StrictUtf8, leaveOpen: true);
        stream.Position = 0;
        var directory = ReadDirectory(reader);
        if (!directory.TryGetExactMember(memberName, out var entry))
        {
            throw new HomecomingPiggException($"PIGG member '{memberName}' was not found.");
        }

        return DecompressMember(reader, entry, memberName);
    }

    internal static byte[] ReadMember(string archivePath, HomecomingPiggArchiveDirectory directory, string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        ArgumentNullException.ThrowIfNull(directory);

        if (!directory.TryGetExactMember(memberName, out var entry))
        {
            throw new HomecomingPiggException($"PIGG member '{memberName}' was not found.");
        }

        using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var reader = new BinaryReader(stream, StrictUtf8, leaveOpen: true);
        return DecompressMember(reader, entry, memberName);
    }

    internal static HomecomingPiggArchiveDirectory ReadDirectory(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("The PIGG source stream must be readable and seekable.", nameof(stream));
        }

        using var reader = new BinaryReader(stream, StrictUtf8, leaveOpen: true);
        stream.Position = 0;
        return ReadDirectory(reader);
    }

    private static HomecomingPiggArchiveDirectory ReadDirectory(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        Require(ReadUInt32(reader, "archive magic") == ArchiveMagic, "Archive magic is invalid.");
        Require(ReadUInt16(reader, "archive version") == SupportedVersion, "Archive version is not supported.");
        Require(ReadUInt16(reader, "archive read version") == SupportedVersion, "Archive read version is not supported.");
        Require(ReadUInt16(reader, "archive header length") == ArchiveHeaderLength, "Archive header length is invalid.");

        var entryHeaderLength = ReadUInt16(reader, "entry header length");
        Require(entryHeaderLength >= MinimumEntryHeaderLength, "Entry header length is invalid.");

        var entryCount = ReadUInt32(reader, "entry count");
        Require(entryCount <= int.MaxValue, "Entry count is too large.");

        var directoryEnd = checked((long)ArchiveHeaderLength + ((long)entryCount * entryHeaderLength));
        Require(directoryEnd <= stream.Length, "The entry directory extends beyond the archive.");

        var entries = new PiggEntry[entryCount];
        for (var index = 0; index < entries.Length; index++)
        {
            var headerStart = stream.Position;
            Require(ReadUInt32(reader, $"entry {index} magic") == EntryMagic, $"Entry {index} magic is invalid.");

            var nameIndex = ReadUInt32(reader, $"entry {index} name index");
            var size = ReadUInt32(reader, $"entry {index} size");
            _ = ReadUInt32(reader, $"entry {index} timestamp");
            var offset = ReadUInt32(reader, $"entry {index} offset");
            _ = ReadUInt32(reader, $"entry {index} reserved field");
            _ = ReadUInt32(reader, $"entry {index} header data index");
            ReadExactly(reader, 16, $"entry {index} checksum");
            var packedSize = ReadUInt32(reader, $"entry {index} packed size");

            entries[index] = new PiggEntry(nameIndex, size, offset, packedSize);
            stream.Position = checked(headerStart + entryHeaderLength);
        }

        Require(ReadUInt32(reader, "string table magic") == StringTableMagic, "String table magic is invalid.");
        var nameCount = ReadUInt32(reader, "string table count");
        var tableLength = ReadUInt32(reader, "string table length");
        Require(nameCount <= int.MaxValue, "String table count is too large.");

        var names = new string[nameCount];
        long consumedTableBytes = 0;
        for (var index = 0; index < names.Length; index++)
        {
            var length = ReadUInt32(reader, $"name {index} length");
            consumedTableBytes = checked(consumedTableBytes + sizeof(uint) + length);
            Require(consumedTableBytes <= tableLength, "String table entries exceed the declared length.");
            Require(length is > 0 and <= int.MaxValue, $"Name {index} length is invalid.");

            var bytes = ReadExactly(reader, checked((int)length), $"name {index}");
            Require(bytes[^1] == 0, $"Name {index} is not null terminated.");
            names[index] = StrictUtf8.GetString(bytes, 0, bytes.Length - 1);
        }

        Require(consumedTableBytes == tableLength, "String table length does not match its entries.");

        return new HomecomingPiggArchiveDirectory(names, entries);
    }

    private static byte[] DecompressMember(BinaryReader reader, PiggEntry entry, string memberName)
    {
        Require(entry.Size <= int.MaxValue, $"PIGG member '{memberName}' is too large.");
        Require(entry.PackedSize <= int.MaxValue, $"Packed PIGG member '{memberName}' is too large.");

        var storedLength = entry.PackedSize == 0 ? entry.Size : entry.PackedSize;
        Require((long)entry.Offset + storedLength <= reader.BaseStream.Length, $"PIGG member '{memberName}' extends beyond the archive.");

        reader.BaseStream.Position = entry.Offset;
        var packed = ReadExactly(reader, checked((int)storedLength), $"packed member '{memberName}'");

        if (entry.PackedSize == 0)
        {
            Require(packed.Length == entry.Size, $"PIGG member '{memberName}' uncompressed size mismatch.");
            return packed;
        }

        using var input = new MemoryStream(packed, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);

        var result = new byte[entry.Size];
        var bytesRead = 0;
        try
        {
            while (bytesRead < result.Length)
            {
                var read = zlib.Read(result, bytesRead, result.Length - bytesRead);
                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }

            Require(bytesRead == result.Length, $"PIGG member '{memberName}' decompressed to fewer bytes than declared.");
            Require(zlib.ReadByte() == -1, $"PIGG member '{memberName}' decompressed to more bytes than declared.");
            return result;
        }
        catch (InvalidDataException exception)
        {
            throw new HomecomingPiggException(
                $"PIGG member '{memberName}' has invalid zlib-compressed data: {exception.Message}",
                exception);
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
            throw new HomecomingPiggException($"Archive is truncated while reading {field}.");
        }

        return bytes;
    }

    private static void EnsureAvailable(BinaryReader reader, long length, string field)
    {
        if (length < 0 || reader.BaseStream.Position > reader.BaseStream.Length - length)
        {
            throw new HomecomingPiggException($"Archive is truncated while reading {field}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new HomecomingPiggException(message);
        }
    }

    internal readonly record struct PiggEntry(uint NameIndex, uint Size, uint Offset, uint PackedSize);
}

internal sealed class HomecomingPiggArchiveDirectory
{
    private readonly Dictionary<string, string> _membersByOrdinalIgnoreCase;

    internal HomecomingPiggArchiveDirectory(string[] names, HomecomingPiggMemberReader.PiggEntry[] entries)
    {
        Names = names;
        Entries = entries;
        _membersByOrdinalIgnoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var memberName = names[entry.NameIndex];
            _membersByOrdinalIgnoreCase.TryAdd(memberName, memberName);
        }
    }

    internal string[] Names { get; }

    internal HomecomingPiggMemberReader.PiggEntry[] Entries { get; }

    internal bool TryGetExactMember(string memberName, out HomecomingPiggMemberReader.PiggEntry entry)
    {
        entry = default;
        for (var index = 0; index < Entries.Length; index++)
        {
            var candidate = Entries[index];
            if (!string.Equals(Names[candidate.NameIndex], memberName, StringComparison.Ordinal))
            {
                continue;
            }

            entry = candidate;
            return true;
        }

        return false;
    }

    internal bool TryResolveMemberPath(string memberPath, out string resolvedMemberPath)
    {
        if (_membersByOrdinalIgnoreCase.TryGetValue(memberPath, out resolvedMemberPath!))
        {
            return true;
        }

        resolvedMemberPath = string.Empty;
        return false;
    }
}

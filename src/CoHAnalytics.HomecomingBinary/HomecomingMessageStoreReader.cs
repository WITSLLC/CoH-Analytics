using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace CoHAnalytics.HomecomingBinary;

internal static class HomecomingMessageStoreReader
{
    private const uint Parse7MessageStoreFormat = 20090521;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static HomecomingMessageStore Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream, StrictUtf8);

            Require(ReadUInt32(reader, "format identifier") == Parse7MessageStoreFormat, "Parse7 message store format identifier is invalid.");
            var strings = ReadStringPool(reader, "message string pool");
            var variables = ReadStringPool(reader, "variable string pool");
            var recordCount = ReadUInt32(reader, "message count");
            Require(recordCount <= int.MaxValue, "Message count is too large.");
            Require(recordCount <= (stream.Length - stream.Position) / 16, "Message count exceeds the remaining data.");

            var messages = new Dictionary<string, string>(checked((int)recordCount), StringComparer.Ordinal);
            for (var index = 0; index < recordCount; index++)
            {
                var key = ReadLengthPrefixedString(reader, $"message {index} key");
                Require(key.Length > 0, $"Message {index} has an empty key.");
                Require(!key.Contains('\0', StringComparison.Ordinal), $"Message {index} key contains a null character.");

                var valueIndex = ReadUInt32(reader, $"message {index} value index");
                var helpIndex = ReadUInt32(reader, $"message {index} help index");
                Require(valueIndex < strings.Count, $"Message '{key}' refers to an invalid value index.");
                Require(helpIndex < strings.Count, $"Message '{key}' refers to an invalid help index.");

                var variableCount = ReadUInt32(reader, $"message {index} variable count");
                Require(variableCount <= (stream.Length - stream.Position) / sizeof(uint), $"Message '{key}' variable list exceeds the remaining data.");
                for (var variableIndex = 0; variableIndex < variableCount; variableIndex++)
                {
                    var poolIndex = ReadUInt32(reader, $"message '{key}' variable {variableIndex}");
                    Require(poolIndex < variables.Count, $"Message '{key}' refers to an invalid variable index.");
                }

                Require(messages.TryAdd(key, strings[checked((int)valueIndex)]), $"Parse7 message store contains duplicate key '{key}'.");
            }

            Require(stream.Position == stream.Length, "Parse7 message store contains trailing data.");
            return new HomecomingMessageStore(messages);
        }
        catch (HomecomingMessageStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or DecoderFallbackException)
        {
            throw new HomecomingMessageStoreException(
                $"Parse7 message store is malformed or truncated: {exception.Message}",
                exception);
        }
    }

    private static IReadOnlyList<string> ReadStringPool(BinaryReader reader, string field)
    {
        var declaredCount = ReadUInt32(reader, $"{field} count");
        var byteLength = ReadUInt32(reader, $"{field} byte length");
        Require(declaredCount <= int.MaxValue, $"{field} count is too large.");
        Require(byteLength <= int.MaxValue, $"{field} is too large.");

        var bytes = ReadExactly(reader, checked((int)byteLength), field);
        if (declaredCount == 0)
        {
            Require(bytes.Length == 0, $"{field} count does not match its contents.");
            return [];
        }

        Require(bytes.Length > 0 && bytes[^1] == 0, $"{field} is not null terminated.");
        var decoded = StrictUtf8.GetString(bytes);
        var values = decoded.Split('\0', StringSplitOptions.None);
        Require(values[^1].Length == 0, $"{field} is not null terminated.");
        Require(values.Length - 1 == declaredCount, $"{field} count does not match its contents.");
        return values[..^1];
    }

    private static string ReadLengthPrefixedString(BinaryReader reader, string field)
    {
        var byteLength = ReadUInt32(reader, $"{field} length");
        Require(byteLength <= int.MaxValue, $"{field} is too large.");
        return StrictUtf8.GetString(ReadExactly(reader, checked((int)byteLength), field));
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
            throw new HomecomingMessageStoreException($"Parse7 message store is truncated while reading {field}.");
        }

        return bytes;
    }

    private static void EnsureAvailable(BinaryReader reader, long length, string field)
    {
        if (length < 0 || reader.BaseStream.Position > reader.BaseStream.Length - length)
        {
            throw new HomecomingMessageStoreException($"Parse7 message store is truncated while reading {field}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new HomecomingMessageStoreException(message);
        }
    }
}

internal sealed class HomecomingMessageStore
{
    private readonly IReadOnlyDictionary<string, string> _messages;

    internal HomecomingMessageStore(IDictionary<string, string> messages)
    {
        _messages = new ReadOnlyDictionary<string, string>(messages);
    }

    internal int Count => _messages.Count;

    internal bool TryResolve(string key, out string message) =>
        _messages.TryGetValue(key, out message!);
}

internal sealed class HomecomingMessageStoreException : Exception
{
    internal HomecomingMessageStoreException(string message)
        : base(message)
    {
    }

    internal HomecomingMessageStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

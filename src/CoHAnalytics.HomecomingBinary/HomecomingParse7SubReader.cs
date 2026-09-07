using System.IO;
using System.Text;

namespace CoHAnalytics.HomecomingBinary;

internal sealed class HomecomingParse7SubReader
{
    private readonly byte[] _data;
    private readonly int _end;
    private readonly HomecomingParse7StringPool _stringPool;
    private int _position;

    internal HomecomingParse7SubReader(
        byte[] data,
        HomecomingParse7StringPool stringPool,
        int offset,
        int length)
    {
        _data = data;
        _stringPool = stringPool;
        _position = offset;
        _end = checked(offset + length);
    }

    internal int Position => _position;

    internal int Remaining => _end - _position;

    internal uint ReadUInt32(string field)
    {
        EnsureAvailable(sizeof(uint), field);
        var value = BitConverter.ToUInt32(_data, _position);
        _position += sizeof(uint);
        return value;
    }

    internal float ReadSingle(string field)
    {
        EnsureAvailable(sizeof(float), field);
        var result = BitConverter.ToSingle(_data, _position);
        _position += sizeof(float);
        return result;
    }

    internal int ReadInt32(string field)
    {
        EnsureAvailable(sizeof(int), field);
        var result = BitConverter.ToInt32(_data, _position);
        _position += sizeof(int);
        return result;
    }

    internal bool ReadBool(string field)
    {
        EnsureAvailable(sizeof(uint), field);
        var value = (_data[_position] & 1) != 0;
        _position += sizeof(uint);
        return value;
    }

    internal string ReadString(string field)
    {
        var offset = ReadUInt32(field);
        return offset == 0
            ? string.Empty
            : _stringPool.Resolve(offset, field);
    }

    internal IReadOnlyList<uint> ReadUInt32Array(string field)
    {
        var count = ReadUInt32($"{field} count");
        var values = new List<uint>(checked((int)count));
        for (var index = 0; index < count; index++)
        {
            values.Add(ReadUInt32($"{field} {index}"));
        }

        return values;
    }

    internal IReadOnlyList<string> ReadStringArray(string field)
    {
        var count = ReadUInt32($"{field} count");
        var values = new List<string>(checked((int)count));
        for (var index = 0; index < count; index++)
        {
            values.Add(ReadString($"{field} {index}"));
        }

        return values;
    }

    internal void SkipStructArray(string field)
    {
        var count = ReadUInt32($"{field} count");
        for (var index = 0; index < count; index++)
        {
            var length = ReadUInt32($"{field} {index} length");
            Skip(checked((int)length), $"{field} {index}");
        }
    }

    internal void Skip(int length, string field)
    {
        EnsureAvailable(length, field);
        _position = checked(_position + length);
    }

    internal void SkipToEnd() => _position = _end;

    internal HomecomingParse7SubReader SubReader(uint length, string field)
    {
        EnsureAvailable(checked((int)length), field);
        var subReader = new HomecomingParse7SubReader(_data, _stringPool, _position, checked((int)length));
        _position = checked(_position + (int)length);
        return subReader;
    }

    private void EnsureAvailable(int length, string field)
    {
        if (length < 0 || _position > _end - length)
        {
            throw new HomecomingPowersBoostDiscoveryException(
                $"Parse7 record is truncated while reading {field}.");
        }
    }
}

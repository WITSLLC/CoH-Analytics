using System.IO;
using System.Text;

namespace CoHAnalytics.ReferenceDataGenerator;

/// <summary>
/// Discovery-only reader for <c>boost_effect_above.bin</c>,
/// <c>boost_effect_below.bin</c>, and <c>boost_effect_boosters.bin</c>.
/// Layout: Parse7 header, then <c>u32 blockLen; u32 count; f32[count]</c>.
/// </summary>
internal static class HomecomingBoostEffectCurveDiscoveryReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyList<float> Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream, StrictUtf8);
            _ = HomecomingParse7HeaderReader.Read(reader, "BoostEffectCurve Parse7");
            var blockLength = ReadUInt32(reader, "BoostEffectCurve block length");
            var blockEnd = checked(stream.Position + blockLength);
            Require(blockEnd <= stream.Length, "BoostEffectCurve block extends beyond the input.");

            var count = ReadUInt32(reader, "BoostEffectCurve count");
            Require(count is > 0 and <= 64, $"BoostEffectCurve count {count} is implausible.");

            var values = new float[checked((int)count)];
            for (var index = 0; index < values.Length; index++)
            {
                EnsureAvailable(reader, sizeof(float), $"BoostEffectCurve value {index}");
                values[index] = reader.ReadSingle();
            }

            Require(stream.Position == blockEnd, "BoostEffectCurve block length does not match values.");
            return values;
        }
        catch (HomecomingBoostEffectCurveDiscoveryException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or InvalidDataException
                or DecoderFallbackException or OverflowException)
        {
            throw new HomecomingBoostEffectCurveDiscoveryException(
                $"BoostEffectCurve Parse7 data is malformed or truncated: {exception.Message}",
                exception);
        }
    }

    private static uint ReadUInt32(BinaryReader reader, string field)
    {
        EnsureAvailable(reader, sizeof(uint), field);
        return reader.ReadUInt32();
    }

    private static void EnsureAvailable(BinaryReader reader, long length, string field)
    {
        if (length < 0 || reader.BaseStream.Position > reader.BaseStream.Length - length)
        {
            throw new HomecomingBoostEffectCurveDiscoveryException(
                $"BoostEffectCurve Parse7 data is truncated while reading {field}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new HomecomingBoostEffectCurveDiscoveryException(message);
        }
    }
}

internal sealed class HomecomingBoostEffectCurveDiscoveryException : Exception
{
    internal HomecomingBoostEffectCurveDiscoveryException(string message)
        : base(message)
    {
    }

    internal HomecomingBoostEffectCurveDiscoveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

using System.IO;
using System.Text;
using CoHAnalytics.HomecomingBinary;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingPowersReader
{
    private const int RequiredPowerBytes = 10 * sizeof(uint);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyList<HomecomingConcreteBoostRecord> ReadBoosts(byte[] data)
        => ReadIdentities(data).Boosts;

    internal static IReadOnlyList<HomecomingConcreteInspirationRecord> ReadInspirations(byte[] data)
        => ReadIdentities(data).Inspirations;

    internal static HomecomingPowerIdentities ReadIdentities(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream, StrictUtf8);
            var stringPool = HomecomingParse7HeaderReader.Read(reader, "Powers Parse7");
            var blockLength = ReadUInt32(reader, "Powers definition block length");
            var blockEnd = checked(stream.Position + blockLength);
            Require(blockEnd <= stream.Length, "Powers definition block extends beyond the input.");

            var recordCount = ReadUInt32(reader, "Power record count");
            Require(recordCount <= int.MaxValue, "Power record count is too large.");
            Require(recordCount <= (blockEnd - stream.Position) / sizeof(uint),
                "Power record count exceeds the definition block.");

            var boosts = new List<HomecomingConcreteBoostRecord>();
            var boostSourceIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < recordCount; index++)
            {
                var recordLength = ReadUInt32(reader, $"Power record {index} length");
                var recordEnd = checked(stream.Position + recordLength);
                Require(recordEnd <= blockEnd, $"Power record {index} extends beyond the definition block.");
                Require(recordLength >= sizeof(uint),
                    $"Power record {index} is shorter than its required source ID.");

                var sourceIdOffset = ReadUInt32(reader, $"Power record {index} source ID");
                var sourceId = stringPool.Resolve(sourceIdOffset, $"record {index} source ID");
                if (sourceId.StartsWith("Boosts.", StringComparison.Ordinal))
                {
                    Require(recordLength >= RequiredPowerBytes,
                        $"Power record '{sourceId}' is shorter than the required fields.");
                    stream.Position = checked(recordEnd - recordLength + (9 * sizeof(uint)));
                    var displayNameOffset = ReadUInt32(
                        reader,
                        $"Power record '{sourceId}' display name");
                    var displayNameMessageKey = stringPool.Resolve(
                        displayNameOffset,
                        $"Power record '{sourceId}' display name");
                    Require(displayNameMessageKey.Length > 0,
                        $"Power record '{sourceId}' has an empty display-name message key.");
                    Require(sourceId.Length > "Boosts.".Length,
                        $"Boost record {index} has an empty source identity.");
                    Require(boostSourceIds.Add(sourceId),
                        $"Boost source ID '{sourceId}' is duplicated.");
                    boosts.Add(new HomecomingConcreteBoostRecord(
                        sourceId,
                        displayNameMessageKey,
                        ReadSourceForm(sourceId)));
                }

                stream.Position = recordEnd;
            }

            Require(stream.Position == blockEnd,
                "Powers definition block length does not match its records.");
            Require(blockEnd == stream.Length, "Powers Parse7 data contains trailing bytes.");

            var inspirations = HomecomingPowersInspirationDiscoveryReader.ReadInspirations(data)
                .Select(ToConcreteInspirationRecord)
                .ToArray();
            return new HomecomingPowerIdentities(boosts, inspirations);
        }
        catch (HomecomingPowersBoostDiscoveryException exception)
        {
            throw new HomecomingPowersException(
                exception.Message,
                exception);
        }
        catch (HomecomingPowersException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or InvalidDataException
                or DecoderFallbackException or OverflowException)
        {
            throw new HomecomingPowersException(
                $"Powers Parse7 data is malformed or truncated: {exception.Message}",
                exception);
        }
    }

    private static HomecomingConcreteInspirationRecord ToConcreteInspirationRecord(
        HomecomingInspirationDiscoveryRecord record)
    {
        Require(record.SourceId.Length > "Inspirations.".Length,
            $"Inspiration record '{record.SourceId}' has an empty source identity.");
        Require(record.DisplayNameMessageKey.Length > 0,
            $"Inspiration record '{record.SourceId}' has an empty display-name message key.");

        var category = HomecomingInspirationSourceMetadata.ParseHomecomingCategory(record.SourceId);
        return new HomecomingConcreteInspirationRecord(
            record.SourceId,
            record.DisplayNameMessageKey,
            NormalizeOptionalMessageKey(record.DisplayHelpMessageKey),
            NormalizeOptionalMessageKey(record.ShortHelpMessageKey),
            NormalizeOptionalIcon(record.Icon),
            category,
            HomecomingInspirationSourceMetadata.ParseStandardTier(category),
            HomecomingInspirationSourceMetadata.ParseInspirationForm(category));
    }

    private static string? NormalizeOptionalMessageKey(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? NormalizeOptionalIcon(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ReadSourceForm(string sourceId)
    {
        var firstSeparator = sourceId.IndexOf('.', StringComparison.Ordinal);
        var secondSeparator = sourceId.IndexOf('.', firstSeparator + 1);
        Require(firstSeparator >= 0 && secondSeparator > firstSeparator + 1,
            $"Boost source ID '{sourceId}' does not contain a concrete identity segment.");
        var concreteName = sourceId[(firstSeparator + 1)..secondSeparator];
        const string superiorAttunedPrefix = "Superior_Attuned_";
        if (concreteName.StartsWith(superiorAttunedPrefix, StringComparison.Ordinal))
        {
            return "Superior_Attuned";
        }

        var separator = concreteName.IndexOf('_', StringComparison.Ordinal);
        return separator > 0 ? concreteName[..separator] : concreteName;
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
            throw new HomecomingPowersException(
                $"Powers Parse7 data is truncated while reading {field}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new HomecomingPowersException(message);
        }
    }
}

internal sealed record HomecomingConcreteBoostRecord(
    string HomecomingSourceId,
    string DisplayNameMessageKey,
    string SourceForm);

internal sealed record HomecomingConcreteInspirationRecord(
    string HomecomingSourceId,
    string DisplayNameMessageKey,
    string? DisplayHelpMessageKey,
    string? ShortHelpMessageKey,
    string? IconIdentity,
    string HomecomingCategory,
    string? StandardTier,
    string InspirationForm);

internal sealed record HomecomingPowerIdentities(
    IReadOnlyList<HomecomingConcreteBoostRecord> Boosts,
    IReadOnlyList<HomecomingConcreteInspirationRecord> Inspirations);

internal sealed class HomecomingPowersException : Exception
{
    internal HomecomingPowersException(string message)
        : base(message)
    {
    }

    internal HomecomingPowersException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

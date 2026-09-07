using System.IO;
using System.Text;
using CoHAnalytics.HomecomingBinary;
using CoHAnalytics.Services;

namespace CoHAnalytics.Homecoming;

/// <summary>
/// Indexes archetype-owned powers from the installed game's <c>bin/powers.bin</c> Parse7 export.
/// </summary>
public sealed class HomecomingArchetypePowerReferenceCatalog : IHomecomingArchetypePowerReferenceCatalog
{
    private const string PowersArchiveRelativePath = @"assets\live\bin_powers.pigg";
    private const string PowersMember = "bin/powers.bin";

    private readonly HomecomingInstallationService _installationService;
    private readonly object _sync = new();
    private Dictionary<string, HomecomingArchetypePowerResolution>? _byLookupKey;

    public HomecomingArchetypePowerReferenceCatalog(HomecomingInstallationService installationService)
    {
        _installationService = installationService;
    }

    public bool IsLoaded
    {
        get
        {
            lock (_sync)
            {
                return _byLookupKey is not null;
            }
        }
    }

    public bool TryResolvePower(
        string categoryGroup,
        string? powersetToken,
        string powerName,
        out HomecomingArchetypePowerResolution resolution)
    {
        resolution = default;
        if (string.IsNullOrWhiteSpace(categoryGroup) || string.IsNullOrWhiteSpace(powerName))
        {
            return false;
        }

        var index = GetOrCreateIndex();
        var normalizedCategory = NormalizeLookupSegment(categoryGroup);
        var normalizedPower = NormalizeLookupSegment(powerName);
        if (!string.IsNullOrWhiteSpace(powersetToken))
        {
            var normalizedSet = NormalizeLookupSegment(powersetToken);
            if (index.TryGetValue($"{normalizedCategory}.{normalizedSet}.{normalizedPower}", out resolution))
            {
                return true;
            }
        }

        return index.TryGetValue($"{normalizedCategory}.{normalizedPower}", out resolution);
    }

    private Dictionary<string, HomecomingArchetypePowerResolution> GetOrCreateIndex()
    {
        lock (_sync)
        {
            if (_byLookupKey is not null)
            {
                return _byLookupKey;
            }

            _byLookupKey = TryLoadIndex() ?? new Dictionary<string, HomecomingArchetypePowerResolution>(StringComparer.Ordinal);
            return _byLookupKey;
        }
    }

    private Dictionary<string, HomecomingArchetypePowerResolution>? TryLoadIndex()
    {
        var installRoot = _installationService.CurrentInstallation?.InstallRoot;
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return null;
        }

        var archivePath = Path.Combine(installRoot, PowersArchiveRelativePath);
        if (!File.Exists(archivePath))
        {
            return null;
        }

        try
        {
            var powers = HomecomingPiggMemberReader.ReadMember(archivePath, PowersMember);
            return HomecomingArchetypePowerIndexBuilder.Build(powers);
        }
        catch (HomecomingPiggException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal static string NormalizeLookupSegment(string value) =>
        value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
}

internal static class HomecomingArchetypePowerIndexBuilder
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static Dictionary<string, HomecomingArchetypePowerResolution> Build(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream, StrictUtf8);
        var stringPool = HomecomingParse7HeaderReader.Read(reader, "Powers Parse7");
        var blockLength = reader.ReadUInt32();
        var blockEnd = stream.Position + blockLength;
        var recordCount = reader.ReadUInt32();

        var index = new Dictionary<string, HomecomingArchetypePowerResolution>(StringComparer.Ordinal);
        for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
        {
            var recordLength = reader.ReadUInt32();
            var recordStart = (int)stream.Position;
            var recordEnd = checked(recordStart + recordLength);
            if (recordEnd > blockEnd)
            {
                break;
            }

            var subReader = new HomecomingParse7SubReader(
                data,
                stringPool,
                recordStart,
                checked((int)recordLength));
            var sourceId = subReader.ReadString("source ID");
            IndexSourceId(sourceId, index);
            stream.Position = recordEnd;
        }

        return index;
    }

    private static void IndexSourceId(
        string sourceId,
        Dictionary<string, HomecomingArchetypePowerResolution> index)
    {
        if (string.IsNullOrWhiteSpace(sourceId)
            || sourceId.StartsWith("Boosts.", StringComparison.Ordinal)
            || sourceId.StartsWith("Set_Bonus.", StringComparison.Ordinal)
            || sourceId.StartsWith("Inspirations.", StringComparison.Ordinal))
        {
            return;
        }

        var segments = sourceId.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var segmentIndex = 0; segmentIndex < segments.Length - 2; segmentIndex++)
        {
            var categoryGroup = segments[segmentIndex];
            if (!IsArchetypeCategoryGroup(categoryGroup))
            {
                continue;
            }

            var powersetId = segments[segmentIndex + 1];
            var powerName = segments[segmentIndex + 2];
            if (IsExcludedPowersetSegment(powersetId) || IsExcludedPowersetSegment(powerName))
            {
                continue;
            }

            var resolution = new HomecomingArchetypePowerResolution(
                categoryGroup,
                powersetId,
                HomecomingPowersetDisplayNames.Format(powersetId),
                powerName);
            IndexResolution(index, resolution);
        }
    }

    private static void IndexResolution(
        Dictionary<string, HomecomingArchetypePowerResolution> index,
        HomecomingArchetypePowerResolution resolution)
    {
        var category = HomecomingArchetypePowerReferenceCatalog.NormalizeLookupSegment(resolution.CategoryGroup);
        var set = HomecomingArchetypePowerReferenceCatalog.NormalizeLookupSegment(resolution.PowersetId);
        var power = HomecomingArchetypePowerReferenceCatalog.NormalizeLookupSegment(resolution.PowerName);
        index.TryAdd($"{category}.{set}.{power}", resolution);
        index.TryAdd($"{category}.{power}", resolution);
    }

    private static bool IsArchetypeCategoryGroup(string segment)
    {
        if (!segment.Contains('_', StringComparison.Ordinal))
        {
            return false;
        }

        if (segment.StartsWith("Class_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !segment.Equals("Pool", StringComparison.OrdinalIgnoreCase)
            && !segment.Equals("Inherent", StringComparison.OrdinalIgnoreCase)
            && !segment.Equals("Epic", StringComparison.OrdinalIgnoreCase)
            && !segment.Equals("Incarnate", StringComparison.OrdinalIgnoreCase)
            && !segment.Equals("Prestige", StringComparison.OrdinalIgnoreCase)
            && !segment.Equals("Temp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludedPowersetSegment(string segment) =>
        segment.Equals("Pool", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("Inherent", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("Epic", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("Incarnate", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("Prestige", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("Temp", StringComparison.OrdinalIgnoreCase);
}

public static class HomecomingPowersetDisplayNames
{
    internal static string Format(string powersetId)
    {
        if (string.IsNullOrWhiteSpace(powersetId))
        {
            return string.Empty;
        }

        var parts = powersetId.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(
            ' ',
            parts.Select(part => part.Length switch
            {
                0 => string.Empty,
                1 => part.ToUpperInvariant(),
                _ => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()
            }));
    }

    internal static bool IsGenericCategoryDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return true;
        }

        return displayName.Equals("Summon", StringComparison.OrdinalIgnoreCase)
            || displayName.Equals("Buff", StringComparison.OrdinalIgnoreCase)
            || displayName.Equals("Control", StringComparison.OrdinalIgnoreCase)
            || displayName.Equals("Melee", StringComparison.OrdinalIgnoreCase)
            || displayName.Equals("Defense", StringComparison.OrdinalIgnoreCase)
            || displayName.Equals("Assault", StringComparison.OrdinalIgnoreCase)
            || displayName.Equals("Ranged", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsGenericCategorySuffix(string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return true;
        }

        return suffix.Equals("summon", StringComparison.OrdinalIgnoreCase)
            || suffix.Equals("buff", StringComparison.OrdinalIgnoreCase)
            || suffix.Equals("control", StringComparison.OrdinalIgnoreCase)
            || suffix.Equals("melee", StringComparison.OrdinalIgnoreCase)
            || suffix.Equals("defense", StringComparison.OrdinalIgnoreCase)
            || suffix.Equals("assault", StringComparison.OrdinalIgnoreCase)
            || suffix.Equals("ranged", StringComparison.OrdinalIgnoreCase);
    }
}

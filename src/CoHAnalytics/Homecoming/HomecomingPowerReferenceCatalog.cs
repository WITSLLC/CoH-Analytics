using System.IO;
using System.Text;
using CoHAnalytics.HomecomingBinary;
using CoHAnalytics.Services;

namespace CoHAnalytics.Homecoming;

/// <summary>
/// Resolves full raw power identities against authoritative presentation data in the installed client.
/// </summary>
public sealed class HomecomingPowerReferenceCatalog : IHomecomingPowerReferenceCatalog
{
    private const string BinArchiveRelativePath = @"assets\live\bin.pigg";
    private const string PowersArchiveRelativePath = @"assets\live\bin_powers.pigg";
    private const string MessagesMember = "bin/clientmessages-en.bin";
    private const string PowersMember = "bin/powers.bin";
    private const string PowersetsMember = "bin/powersets.bin";

    private readonly HomecomingInstallationService _installationService;
    private readonly object _sync = new();
    private Dictionary<string, HomecomingPowerReference>? _bySourceId;

    public HomecomingPowerReferenceCatalog(HomecomingInstallationService installationService)
    {
        _installationService = installationService;
    }

    public bool IsLoaded
    {
        get
        {
            lock (_sync)
            {
                return _bySourceId is not null;
            }
        }
    }

    public bool TryResolve(
        string categoryId,
        string powersetId,
        string powerId,
        out HomecomingPowerReference power)
    {
        power = default;
        if (!TryCreateSourceId(categoryId, powersetId, powerId, out var sourceId))
        {
            return false;
        }

        return GetOrCreateIndex().TryGetValue(sourceId, out power);
    }

    private Dictionary<string, HomecomingPowerReference> GetOrCreateIndex()
    {
        lock (_sync)
        {
            if (_bySourceId is not null)
            {
                return _bySourceId;
            }

            _bySourceId = TryLoadIndex()
                ?? new Dictionary<string, HomecomingPowerReference>(StringComparer.OrdinalIgnoreCase);
            return _bySourceId;
        }
    }

    private Dictionary<string, HomecomingPowerReference>? TryLoadIndex()
    {
        var installRoot = _installationService.CurrentInstallation?.InstallRoot;
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return null;
        }

        var binArchivePath = Path.Combine(installRoot, BinArchiveRelativePath);
        var powersArchivePath = Path.Combine(installRoot, PowersArchiveRelativePath);
        if (!File.Exists(binArchivePath) || !File.Exists(powersArchivePath))
        {
            return null;
        }

        try
        {
            var messages = HomecomingMessageStoreReader.Read(
                HomecomingPiggMemberReader.ReadMember(binArchivePath, MessagesMember));
            var powersets = HomecomingPowersetDefinitionReader.ReadPresentations(
                HomecomingPiggMemberReader.ReadMember(binArchivePath, PowersetsMember));
            var powers = HomecomingPowerDefinitionReader.ReadPresentations(
                HomecomingPiggMemberReader.ReadMember(powersArchivePath, PowersMember));
            return HomecomingPowerReferenceIndexBuilder.Build(powers, powersets, messages);
        }
        catch (Exception exception) when (
            exception is HomecomingPiggException or HomecomingMessageStoreException
                or EndOfStreamException or IOException
                or InvalidDataException or DecoderFallbackException or OverflowException)
        {
            return null;
        }
    }

    private static bool TryCreateSourceId(
        string categoryId,
        string powersetId,
        string powerId,
        out string sourceId)
    {
        sourceId = string.Empty;
        if (!IsIdentitySegment(categoryId)
            || !IsIdentitySegment(powersetId)
            || !IsIdentitySegment(powerId))
        {
            return false;
        }

        sourceId = $"{categoryId.Trim()}.{powersetId.Trim()}.{powerId.Trim()}";
        return true;
    }

    private static bool IsIdentitySegment(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Contains('.', StringComparison.Ordinal);
}

internal static class HomecomingPowerReferenceIndexBuilder
{
    internal static Dictionary<string, HomecomingPowerReference> Build(
        IReadOnlyList<HomecomingPowerPresentationRecord> powers,
        IReadOnlyList<HomecomingPowersetPresentationRecord> powersets,
        HomecomingMessageStore messages)
    {
        var powersetDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var powerset in powersets)
        {
            if (!string.IsNullOrWhiteSpace(powerset.SourceId)
                && messages.TryResolve(powerset.DisplayNameMessageKey, out var displayName)
                && !string.IsNullOrWhiteSpace(displayName))
            {
                powersetDisplayNames.TryAdd(powerset.SourceId, displayName.Trim());
            }
        }

        var index = new Dictionary<string, HomecomingPowerReference>(StringComparer.OrdinalIgnoreCase);
        foreach (var power in powers)
        {
            var segments = power.SourceId.Split('.', StringSplitOptions.None);
            if (segments.Length != 3
                || segments.Any(string.IsNullOrWhiteSpace)
                || !powersetDisplayNames.TryGetValue(
                    $"{segments[0]}.{segments[1]}",
                    out var powersetDisplayName)
                || !messages.TryResolve(power.DisplayNameMessageKey, out var powerDisplayName)
                || string.IsNullOrWhiteSpace(powerDisplayName))
            {
                continue;
            }

            var reference = new HomecomingPowerReference(
                segments[0],
                segments[1],
                segments[2],
                powersetDisplayName,
                powerDisplayName.Trim(),
                ResolveOptionalMessage(messages, power.DisplayHelpMessageKey),
                NormalizeOptional(power.IconIdentity),
                power.IsAutoIssued,
                power.IsFree,
                ToPowerType(power.PowerType));
            index.TryAdd(power.SourceId, reference);
        }

        return index;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ResolveOptionalMessage(
        HomecomingMessageStore messages,
        string? messageKey) =>
        !string.IsNullOrWhiteSpace(messageKey)
        && messages.TryResolve(messageKey, out var value)
            ? NormalizeOptional(value)
            : null;

    private static HomecomingPowerType ToPowerType(uint value) =>
        value <= (uint)HomecomingPowerType.GlobalBoost
            ? (HomecomingPowerType)value
            : HomecomingPowerType.Unknown;
}

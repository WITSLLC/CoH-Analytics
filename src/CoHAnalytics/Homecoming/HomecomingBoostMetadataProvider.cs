using CoHAnalytics.HomecomingBinary;
using CoHAnalytics.Services;

namespace CoHAnalytics.Homecoming;

public sealed class HomecomingBoostMetadataProvider : IHomecomingBoostMetadataProvider
{
    private const string PowersArchiveRelativePath = @"assets\live\bin_powers.pigg";
    private const string PowersMember = "bin/powers.bin";

    private readonly HomecomingInstallationService _installationService;
    private readonly object _sync = new();
    private Dictionary<string, IReadOnlyList<string>>? _boostsAllowedBySourceId;

    public HomecomingBoostMetadataProvider(HomecomingInstallationService installationService)
    {
        _installationService = installationService;
    }

    public IReadOnlyList<string>? TryGetBoostsAllowed(string? homecomingSourceId)
    {
        if (string.IsNullOrWhiteSpace(homecomingSourceId))
        {
            return null;
        }

        var normalizedSourceId = homecomingSourceId.Trim();
        var index = GetOrCreateIndex();
        return index.TryGetValue(normalizedSourceId, out var boostsAllowed)
            ? boostsAllowed
            : null;
    }

    private Dictionary<string, IReadOnlyList<string>> GetOrCreateIndex()
    {
        lock (_sync)
        {
            if (_boostsAllowedBySourceId is not null)
            {
                return _boostsAllowedBySourceId;
            }

            _boostsAllowedBySourceId = TryLoadIndex() ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            return _boostsAllowedBySourceId;
        }
    }

    private Dictionary<string, IReadOnlyList<string>>? TryLoadIndex()
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
            return HomecomingPowersBoostDiscoveryReader.ReadBoosts(powers)
                .ToDictionary(
                    boost => boost.SourceId,
                    boost => boost.BoostsAllowed,
                    StringComparer.Ordinal);
        }
        catch (HomecomingPiggException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (HomecomingPowersBoostDiscoveryException)
        {
            return null;
        }
    }
}

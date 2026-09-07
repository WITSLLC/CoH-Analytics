using System.Windows.Media;
using CoHAnalytics.Services;

namespace CoHAnalytics.Homecoming;

public sealed class InstalledGameAssetProvider : IInstalledGameAssetProvider
{
    private const int MaxCacheEntries = 512;

    private readonly HomecomingInstallationService _installationService;
    private readonly object _archiveSync = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private HomecomingTextureArtworkArchiveSet? _archiveSet;

    public InstalledGameAssetProvider(HomecomingInstallationService installationService)
    {
        _installationService = installationService;
    }

    public ImageSource? TryResolve(string? iconIdentity)
    {
        if (string.IsNullOrWhiteSpace(iconIdentity))
        {
            return null;
        }

        var normalizedIdentity = iconIdentity.Trim();
        if (_cache.TryGetValue(normalizedIdentity, out var cached))
        {
            return cached.ImageSource;
        }

        var installRoot = _installationService.CurrentInstallation?.InstallRoot;
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            StoreCacheEntry(normalizedIdentity, null);
            return null;
        }

        var archiveSet = GetOrCreateArchiveSet(installRoot);
        if (archiveSet is null)
        {
            StoreCacheEntry(normalizedIdentity, null);
            return null;
        }

        if (!archiveSet.TryReadTextureMember(normalizedIdentity, out var memberBytes, out _))
        {
            StoreCacheEntry(normalizedIdentity, null);
            return null;
        }

        var image = HomecomingTextureMemberDecoder.TryDecodeTextureMember(memberBytes);
        StoreCacheEntry(normalizedIdentity, image);
        return image;
    }

    internal bool TryReadTextureMember(string? iconIdentity, out byte[] memberBytes)
    {
        memberBytes = [];
        if (string.IsNullOrWhiteSpace(iconIdentity))
        {
            return false;
        }

        var normalizedIdentity = iconIdentity.Trim();
        var installRoot = _installationService.CurrentInstallation?.InstallRoot;
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return false;
        }

        var archiveSet = GetOrCreateArchiveSet(installRoot);
        return archiveSet is not null
               && archiveSet.TryReadTextureMember(normalizedIdentity, out memberBytes, out _);
    }

    private HomecomingTextureArtworkArchiveSet? GetOrCreateArchiveSet(string installRoot)
    {
        lock (_archiveSync)
        {
            if (_archiveSet is not null
                && string.Equals(_archiveSet.InstallRoot, installRoot, StringComparison.OrdinalIgnoreCase))
            {
                return _archiveSet;
            }

            _archiveSet = HomecomingTextureArtworkArchiveSet.TryOpen(installRoot);
            return _archiveSet;
        }
    }

    private void StoreCacheEntry(string iconIdentity, ImageSource? imageSource)
    {
        if (_cache.Count >= MaxCacheEntries)
        {
            _cache.Clear();
        }

        _cache[iconIdentity] = new CacheEntry(imageSource);
    }

    private readonly record struct CacheEntry(ImageSource? ImageSource);
}

internal sealed class HomecomingTextureArtworkArchiveSet
{
    private readonly HomecomingPiggArchiveIndex[] _archives;

    private HomecomingTextureArtworkArchiveSet(string installRoot, HomecomingPiggArchiveIndex[] archives)
    {
        InstallRoot = installRoot;
        _archives = archives;
    }

    internal string InstallRoot { get; }

    internal static HomecomingTextureArtworkArchiveSet? TryOpen(string installRoot)
    {
        var archives = new List<HomecomingPiggArchiveIndex>();
        foreach (var relativeArchivePath in HomecomingTextureArtworkArchives.RelativeArchivePaths)
        {
            var archivePath = HomecomingTextureArtworkArchives.ResolveArchivePath(installRoot, relativeArchivePath);
            var archive = HomecomingPiggArchiveIndex.TryOpen(archivePath);
            if (archive is not null)
            {
                archives.Add(archive);
            }
        }

        return archives.Count == 0 ? null : new HomecomingTextureArtworkArchiveSet(installRoot, archives.ToArray());
    }

    internal bool TryReadTextureMember(
        string iconIdentity,
        out byte[] memberBytes,
        out string resolvedMemberPath)
    {
        memberBytes = [];
        resolvedMemberPath = string.Empty;

        foreach (var candidate in HomecomingIconMemberPathNormalizer.CreateMemberPathCandidates(iconIdentity))
        {
            foreach (var archive in _archives)
            {
                if (!archive.TryResolveMemberPath(candidate, out var resolvedPath))
                {
                    continue;
                }

                try
                {
                    memberBytes = archive.ReadMember(resolvedPath);
                    resolvedMemberPath = resolvedPath;
                    return true;
                }
                catch (HomecomingPiggException)
                {
                }
                catch (IOException)
                {
                }
            }
        }

        return false;
    }
}

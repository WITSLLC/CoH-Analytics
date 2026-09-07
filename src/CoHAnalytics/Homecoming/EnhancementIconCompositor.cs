using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoHAnalytics.Homecoming;

public sealed class EnhancementIconCompositor : IEnhancementIconCompositor
{
    private const int MaxCacheEntries = 512;

    private readonly IInstalledGameAssetProvider _assetProvider;
    private readonly Dictionary<CacheKey, CacheEntry> _cache = new();

    public EnhancementIconCompositor(IInstalledGameAssetProvider assetProvider)
    {
        _assetProvider = assetProvider;
    }

    public ImageSource? TryCompose(EnhancementIconCompositionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cacheKey = new CacheKey(
            request.IconIdentity,
            request.PogBoostType,
            request.FrameClass);
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached.ImageSource;
        }

        var composed = ComposeUncached(request);
        StoreCacheEntry(cacheKey, composed);
        return composed;
    }

    private ImageSource? ComposeUncached(EnhancementIconCompositionRequest request)
    {
        if (!EnhancementFrameIdentity.TryResolveHalves(
                request.FrameClass,
                out var leftFrameIdentity,
                out var rightFrameIdentity))
        {
            return null;
        }

        var pogIdentity = EnhancementPogIdentity.TryResolveIdentity(request.PogBoostType);
        if (pogIdentity is null)
        {
            return null;
        }

        var leftFrame = _assetProvider.TryResolve(leftFrameIdentity);
        var rightFrame = _assetProvider.TryResolve(rightFrameIdentity);
        var pog = _assetProvider.TryResolve($"{pogIdentity}.tga");
        var innerArt = _assetProvider.TryResolve(request.IconIdentity);

        var pogPlacement = default(HomecomingTextureOverlayPlacement);
        var innerPlacement = default(HomecomingTextureOverlayPlacement);
        if (_assetProvider is InstalledGameAssetProvider installedProvider)
        {
            if (installedProvider.TryReadTextureMember($"{pogIdentity}.tga", out var pogBytes))
            {
                HomecomingTextureMemberDecoder.TryReadOverlayPlacement(pogBytes, out pogPlacement);
            }

            if (installedProvider.TryReadTextureMember(request.IconIdentity, out var innerBytes))
            {
                HomecomingTextureMemberDecoder.TryReadOverlayPlacement(innerBytes, out innerPlacement);
            }
        }

        return EnhancementIconBitmapComposer.TryCompose(
            leftFrame as BitmapSource,
            rightFrame as BitmapSource,
            pog as BitmapSource,
            innerArt as BitmapSource,
            pogPlacement,
            innerPlacement);
    }

    private void StoreCacheEntry(CacheKey cacheKey, ImageSource? imageSource)
    {
        if (_cache.Count >= MaxCacheEntries)
        {
            _cache.Clear();
        }

        _cache[cacheKey] = new CacheEntry(imageSource);
    }

    private readonly record struct CacheKey(
        string IconIdentity,
        string PogBoostType,
        EnhancementFrameClass FrameClass);

    private readonly record struct CacheEntry(ImageSource? ImageSource);
}

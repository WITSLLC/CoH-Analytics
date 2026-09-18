using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;

namespace CoHAnalytics.ViewModels.Workspaces;

/// <summary>Shared Combat presentation icons. Frozen-build matching is opt-in so Incoming cannot bind enemy hits to the player's slotted powers.</summary>
internal sealed class CombatRowIconSupport(
    IHomecomingPowerReferenceCatalog? powerCatalog = null,
    IInstalledGameAssetProvider? assets = null,
    IItemReferenceCatalog? items = null,
    IEnhancementIconCompositor? compositor = null,
    IHomecomingBoostMetadataProvider? boostMetadata = null)
{
    internal const string GenericDamageIconResource = "CoHAnalytics;component/Assets/Images/Status/generic_icon.png";

    private static readonly object GenericIconGate = new();
    private static ImageSource? _genericDamageIcon;

    internal static ImageSource GenericDamageIcon
    {
        get
        {
            if (_genericDamageIcon is not null) return _genericDamageIcon;
            lock (GenericIconGate)
            {
                return _genericDamageIcon ??= CreateGenericDamageIcon();
            }
        }
    }

    public FrozenBuildPower? TryMatchFrozenPower(CombatPowerAnalysisRow row, FrozenBuildManifest? manifest)
    {
        if (row.Scope != CombatAnalyticsScope.Self || row.IsOverflow || manifest is null) return null;
        var surfaced = manifest.Powers.Where(p =>
            string.Equals(p.SurfacedPowerName, row.PowerName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (surfaced.Length == 1) return surfaced[0];
        if (surfaced.Length != 0) return null;
        FrozenBuildPower? found = null;
        var matches = 0;
        foreach (var candidate in manifest.Powers)
        {
            if (powerCatalog?.TryResolve(candidate.RawCategoryToken, candidate.RawPowerSetToken,
                    candidate.RawPowerToken, out var reference) != true
                || string.IsNullOrWhiteSpace(reference.PowerDisplayName)
                || !string.Equals(reference.PowerDisplayName, row.PowerName, StringComparison.OrdinalIgnoreCase))
                continue;
            matches++;
            found = candidate;
            if (matches > 1) return null;
        }
        return found;
    }

    public ImageSource Resolve(CombatPowerAnalysisRow row, FrozenBuildPower? frozen, FrozenBuildManifest? manifest) =>
        ResolveSpecific(row, frozen, manifest) ?? GenericDamageIcon;

    private ImageSource? ResolveSpecific(CombatPowerAnalysisRow row, FrozenBuildPower? frozen, FrozenBuildManifest? manifest)
    {
        if (frozen is not null) return ResolvePowerIcon(frozen);
        if (row.Scope != CombatAnalyticsScope.Self || row.IsOverflow) return null;
        if (powerCatalog?.TryResolveUniqueDisplayName(row.PowerName, out var unique) == true)
            return ResolveReferenceIcon(unique);
        return ResolveProcIcon(row.PowerName, manifest);
    }

    private ImageSource? ResolvePowerIcon(FrozenBuildPower power) =>
        powerCatalog?.TryResolve(power.RawCategoryToken, power.RawPowerSetToken, power.RawPowerToken, out var reference) == true
            ? ResolveReferenceIcon(reference)
            : null;

    private ImageSource? ResolveReferenceIcon(HomecomingPowerReference reference) =>
        reference.IconIdentity is null ? null : assets?.TryResolve(reference.IconIdentity);

    private ImageSource? ResolveProcIcon(string powerName, FrozenBuildManifest? manifest)
    {
        if (items is null) return null;
        var frozen = manifest?.ProcIdentities.Where(i => string.Equals(i.LogName, powerName, StringComparison.Ordinal)).ToArray() ?? [];
        if (frozen.Length > 1) return null;
        if (frozen.Length == 1)
            return items.TryGetById(frozen[0].CatalogItemId, out var captured) ? ResolveItemIcon(captured) : null;
        return items.TryResolve(powerName, out var resolution) ? ResolveItemIcon(resolution.Item) : null;
    }

    private ImageSource? ResolveItemIcon(ItemReferenceRecord item)
    {
        EnhancementSetReferenceRecord? parentSet = null;
        if (!string.IsNullOrWhiteSpace(item.EnhancementSetId))
            items?.TryGetEnhancementSetById(item.EnhancementSetId, out parentSet);
        return items is null
            ? null
            : ReferenceEnhancementBrowseSupport.ResolveComposedIconSource(
                compositor, items, item, parentSet, boostMetadata);
    }

    private static ImageSource CreateGenericDamageIcon()
    {
        Application.ResourceAssembly ??= typeof(CombatRowIconSupport).Assembly;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = AssetUri.ForResource(GenericDamageIconResource);
        image.EndInit();
        image.Freeze();
        return image;
    }
}

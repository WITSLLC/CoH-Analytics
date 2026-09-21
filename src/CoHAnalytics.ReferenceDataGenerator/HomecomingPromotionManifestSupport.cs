using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingPromotionManifestSupport
{
    private const string CatalogVersionPrefix = "item-ref-";

    internal static bool ApplyPromotionRelease(
        ItemReferenceCatalogDocument document,
        string catalogVersion,
        string sourceRevision,
        string sourceNotes,
        string buildVersion)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNotes);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildVersion);

        if (!TryParseCatalogVersion(catalogVersion, out var promotionVersion))
        {
            throw new InvalidOperationException(
                $"Promotion catalog version '{catalogVersion}' is not a supported item-ref version.");
        }

        if (document.Manifest is not null
            && !string.IsNullOrWhiteSpace(document.Manifest.CatalogVersion)
            && (!TryParseCatalogVersion(document.Manifest.CatalogVersion, out var currentVersion)
                || currentVersion > promotionVersion))
        {
            return false;
        }

        document.Manifest ??= new ItemReferenceManifestDocument();
        document.Manifest.CatalogVersion = catalogVersion;
        document.Manifest.SourceRevision = sourceRevision;
        document.Manifest.SourceNotes = sourceNotes;
        document.Manifest.HomecomingCompatibility = new ItemReferenceHomecomingCompatibilityDocument
        {
            BuildMin = buildVersion,
            BuildMax = buildVersion
        };
        return true;
    }

    private static bool TryParseCatalogVersion(string value, out Version version)
    {
        version = null!;
        var normalized = value.Trim();
        return normalized.StartsWith(CatalogVersionPrefix, StringComparison.Ordinal)
               && Version.TryParse(normalized[CatalogVersionPrefix.Length..], out version!);
    }
}

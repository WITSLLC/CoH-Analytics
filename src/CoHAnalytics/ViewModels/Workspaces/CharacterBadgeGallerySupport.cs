using System.Globalization;
using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ViewModels.Workspaces;

public static class CharacterBadgeGallerySupport
{
    private static readonly Dictionary<string, int> CategorySortOrder =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Accolades"] = 0,
            ["Exploration"] = 1,
            ["History"] = 2,
            ["Achievement"] = 3,
            ["Accomplishment"] = 4,
            ["Day Jobs"] = 5,
            ["Defeats"] = 6,
            ["Defeat"] = 6,
            ["PvP"] = 7,
            ["Events"] = 8,
            ["Gladiator"] = 9,
            ["Architect Entertainment"] = 10,
            ["Ouroboros"] = 11,
            ["Invention"] = 12,
            ["Consignment"] = 13,
            ["Veteran"] = 14,
            ["Other"] = 999
        };

    public static CharacterBadgeGalleryResult Build(
        IReadOnlyCollection<string> acquiredBadgeIds,
        IItemReferenceCatalog catalog,
        IInstalledGameAssetProvider? installedGameAssetProvider) =>
        Build(
            acquiredBadgeIds
                .Select(badgeId => new CharacterBadgeAcquisitionEntry
                {
                    CatalogItemId = badgeId,
                    FirstObservedAt = DateTimeOffset.MinValue,
                    ObservedTitle = badgeId,
                    Provenance = CharacterBadgeAcquisitionProvenance.LegacyUnknown
                })
                .ToArray(),
            catalog,
            installedGameAssetProvider);

    public static CharacterBadgeGalleryResult Build(
        IReadOnlyCollection<CharacterBadgeAcquisitionEntry> acquisitions,
        IItemReferenceCatalog catalog,
        IInstalledGameAssetProvider? installedGameAssetProvider)
    {
        var groups = new Dictionary<string, List<CharacterBadgeGalleryBadgeItem>>(StringComparer.OrdinalIgnoreCase);
        var otherCategoryCount = 0;

        foreach (var acquisition in acquisitions)
        {
            var badgeId = acquisition.CatalogItemId;
            catalog.TryGetBadgeById(badgeId, out var badge);
            var category = ResolveCategoryDisplayName(badge);
            if (string.Equals(category, "Other", StringComparison.OrdinalIgnoreCase))
            {
                otherCategoryCount++;
            }

            var displayName = badge is null
                ? acquisition.ObservedTitle
                : acquisition.Provenance == CharacterBadgeAcquisitionProvenance.LogReceipt
                    ? acquisition.ObservedTitle
                    : BadgePresentationNameSupport.GetNeutralDisplayName(catalog, badge);
            var iconIdentity = badge?.HeroIcon;
            var iconSource = ResolveIconSource(installedGameAssetProvider, iconIdentity);
            var useWideBadgeIcon = UsesWideBadgeArtwork(iconIdentity);
            var placeholderLetter = string.IsNullOrWhiteSpace(displayName)
                ? "?"
                : char.ToUpperInvariant(displayName[0]).ToString();

            if (!groups.TryGetValue(category, out var badges))
            {
                badges = [];
                groups[category] = badges;
            }

            badges.Add(new CharacterBadgeGalleryBadgeItem(
                displayName,
                iconSource,
                useWideBadgeIcon,
                placeholderLetter,
                ResolveBadgeSequence(category, badge)));
        }

        var categories = groups
            .Select(pair => new CharacterBadgeGalleryCategoryGroup(
                pair.Key,
                pair.Value
                    .OrderBy(item => item.SequenceOrder ?? int.MaxValue)
                    .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderBy(group => GetCategorySortOrder(group.CategoryName))
            .ThenBy(group => group.CategoryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var totalCount = acquisitions.Count;
        return new CharacterBadgeGalleryResult(
            totalCount,
            FormatBadgeCountLabel(totalCount),
            categories,
            otherCategoryCount);
    }

    public static string FormatBadgeCountLabel(int count) =>
        count == 1 ? "1 Badge Earned" : $"{count} Badges Earned";

    private static string ResolveCategoryDisplayName(BadgeReferenceRecord? badge)
    {
        if (badge is null)
        {
            return "Other";
        }

        if (!string.IsNullOrWhiteSpace(badge.CanonicalCategory))
        {
            var normalized = NormalizeCanonicalCategory(badge.CanonicalCategory.Trim());
            if (normalized is not null)
            {
                return normalized;
            }
        }

        return FormatReferenceKind(badge.ReferenceKind);
    }

    private static string? NormalizeCanonicalCategory(string canonicalCategory)
    {
        switch (canonicalCategory.ToUpperInvariant())
        {
            case "ACHIEVEMENT":
                return "Achievement";
            case "EXPLORATION":
                return "Exploration";
            case "GLADIATOR":
                return "Gladiator";
            case "PVP":
                return "PvP";
            case "VETERAN":
                return "Veteran";
            case "EVENT":
                return "Events";
            case "ARCHITECT":
                return "Architect Entertainment";
            case "DAYJOB":
                return "Day Jobs";
            case "DEFEAT":
                return "Defeat";
            case "INTERNAL":
            case "PERK":
            case "TOURISM":
                return "Other";
            default:
                if (canonicalCategory.Any(char.IsLower) || canonicalCategory.Contains(' ', StringComparison.Ordinal))
                {
                    return canonicalCategory;
                }

                return "Other";
        }
    }

    private static string FormatReferenceKind(ReferenceBadgeKind referenceKind)
    {
        switch (referenceKind)
        {
            case ReferenceBadgeKind.ExplorationBadge:
                return "Exploration";
            case ReferenceBadgeKind.Accolade:
                return "Accolades";
            case ReferenceBadgeKind.Achievement:
                return "Achievement";
            case ReferenceBadgeKind.Accomplishment:
                return "Accomplishment";
            case ReferenceBadgeKind.ArchitectEntertainment:
                return "Architect Entertainment";
            case ReferenceBadgeKind.Consignment:
                return "Consignment";
            case ReferenceBadgeKind.DayJobs:
                return "Day Jobs";
            case ReferenceBadgeKind.Defeats:
                return "Defeats";
            case ReferenceBadgeKind.Events:
                return "Events";
            case ReferenceBadgeKind.Gladiator:
                return "Gladiator";
            case ReferenceBadgeKind.History:
            case ReferenceBadgeKind.HistoryPlaque:
                return "History";
            case ReferenceBadgeKind.Invention:
                return "Invention";
            case ReferenceBadgeKind.Ouroboros:
                return "Ouroboros";
            case ReferenceBadgeKind.Pvp:
                return "PvP";
            case ReferenceBadgeKind.Veteran:
                return "Veteran";
            default:
                return "Other";
        }
    }

    private static int GetCategorySortOrder(string categoryName) =>
        CategorySortOrder.TryGetValue(categoryName, out var order) ? order : 500;

    private static int? ResolveBadgeSequence(string category, BadgeReferenceRecord? badge)
    {
        const string veteranPrefix = "Veteran";
        if (!string.Equals(category, veteranPrefix, StringComparison.OrdinalIgnoreCase)
            || badge?.HomecomingSourceId is not { } sourceId
            || !sourceId.StartsWith(veteranPrefix, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(
                sourceId.AsSpan(veteranPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var veteranLevel))
        {
            return null;
        }

        return veteranLevel;
    }

    private static ImageSource? ResolveIconSource(
        IInstalledGameAssetProvider? installedGameAssetProvider,
        string? iconIdentity)
    {
        if (installedGameAssetProvider is null || string.IsNullOrWhiteSpace(iconIdentity))
        {
            return null;
        }

        return installedGameAssetProvider.TryResolve(iconIdentity);
    }

    private static bool UsesWideBadgeArtwork(string? iconIdentity) =>
        !string.IsNullOrWhiteSpace(iconIdentity)
        && iconIdentity.Contains("Accolade", StringComparison.OrdinalIgnoreCase);
}

public sealed record CharacterBadgeGalleryBadgeItem(
    string DisplayName,
    ImageSource? IconSource,
    bool UseWideBadgeIcon,
    string IconPlaceholderLetter,
    int? SequenceOrder);

public sealed record CharacterBadgeGalleryCategoryGroup(
    string CategoryName,
    IReadOnlyList<CharacterBadgeGalleryBadgeItem> Badges);

public sealed record CharacterBadgeGalleryResult(
    int TotalCount,
    string CountLabel,
    IReadOnlyList<CharacterBadgeGalleryCategoryGroup> Categories,
    int OtherCategoryCount);

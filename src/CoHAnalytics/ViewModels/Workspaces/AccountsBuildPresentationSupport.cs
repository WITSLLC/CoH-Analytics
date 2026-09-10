using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ViewModels.Workspaces;

public static class AccountsBuildPresentationSupport
{
    public static AccountsBuildPresentation Build(
        HomecomingBuildLayoutSnapshot snapshot,
        string? primaryPowerSetName,
        string? secondaryPowerSetName,
        IHomecomingPowerReferenceCatalog? powerCatalog,
        IInstalledGameAssetProvider? assetProvider,
        IItemReferenceCatalog? itemCatalog,
        IEnhancementIconCompositor? enhancementIconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadataProvider)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var enhancementIndex = BuildEnhancementIndex(itemCatalog);
        var orderedSections = new List<SectionBuilder>();
        var sectionsByIdentity = new Dictionary<PowerSetIdentity, SectionBuilder>();

        foreach (var sourcePower in snapshot.Powers.OrderBy(power => power.SourceOrder))
        {
            var powerReference = default(HomecomingPowerReference);
            var resolved = powerCatalog?.TryResolve(
                    sourcePower.RawCategoryToken,
                    sourcePower.RawPowerSetToken,
                    sourcePower.RawPowerToken,
                    out powerReference) == true;
            var powersetDisplayName = resolved
                ? powerReference.PowersetDisplayName
                : FormatRawToken(sourcePower.RawPowerSetToken);
            var powerDisplayName = resolved
                ? powerReference.PowerDisplayName
                : FormatRawToken(sourcePower.RawPowerToken);
            var identity = new PowerSetIdentity(
                sourcePower.RawCategoryToken,
                sourcePower.RawPowerSetToken);

            if (!sectionsByIdentity.TryGetValue(identity, out var section))
            {
                section = new SectionBuilder(identity, powersetDisplayName);
                sectionsByIdentity.Add(identity, section);
                orderedSections.Add(section);
            }

            var powerIconSource = resolved && powerReference.IconIdentity is not null
                ? assetProvider?.TryResolve(powerReference.IconIdentity)
                : null;
            var slots = sourcePower.Slots
                .OrderBy(slot => slot.SlotOrder)
                .Select(slot => BuildSlot(
                    slot,
                    enhancementIndex,
                    assetProvider,
                    itemCatalog,
                    enhancementIconCompositor,
                    boostMetadataProvider))
                .ToArray();
            var tooltip = resolved
                ? $"{powerReference.PowersetDisplayName}: {powerReference.PowerDisplayName} (Level {sourcePower.AcquisitionLevel})"
                : $"{powerDisplayName} (Level {sourcePower.AcquisitionLevel})";

            section.Powers.Add(new AccountsBuildPowerViewModel(
                powerDisplayName,
                $"Lv {sourcePower.AcquisitionLevel}",
                powerIconSource,
                tooltip,
                slots));
        }

        var primary = FindRoleSection(orderedSections, primaryPowerSetName);
        var secondary = FindRoleSection(
            orderedSections.Where(section => !ReferenceEquals(section, primary)),
            secondaryPowerSetName);
        var additional = orderedSections
            .Where(section => !ReferenceEquals(section, primary) && !ReferenceEquals(section, secondary))
            .Select(section => section.ToViewModel())
            .ToArray();

        return new AccountsBuildPresentation(
            primary?.ToViewModel(),
            secondary?.ToViewModel(),
            additional,
            snapshot.Powers.Count);
    }

    private static AccountsBuildEnhancementSlotViewModel BuildSlot(
        HomecomingBuildSlotSnapshot slot,
        IReadOnlyDictionary<string, EnhancementCatalogMatch> enhancementIndex,
        IInstalledGameAssetProvider? assetProvider,
        IItemReferenceCatalog? itemCatalog,
        IEnhancementIconCompositor? enhancementIconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadataProvider)
    {
        if (slot.IsEmpty)
        {
            return new AccountsBuildEnhancementSlotViewModel(
                assetProvider?.TryResolve(EnhancementIconIdentity.EmptySlot),
                "Empty enhancement slot",
                true);
        }

        var rawToken = slot.RawEnhancementToken ?? string.Empty;
        if (!enhancementIndex.TryGetValue(rawToken, out var match))
        {
            return new AccountsBuildEnhancementSlotViewModel(
                null,
                FormatEnhancementTooltip(FormatRawToken(rawToken), slot),
                false);
        }

        EnhancementSetReferenceRecord? parentSet = null;
        if (itemCatalog is not null && match.Item.EnhancementSetId is not null)
        {
            itemCatalog.TryGetEnhancementSetById(match.Item.EnhancementSetId, out parentSet!);
        }

        var iconSource = EnhancementIconCompositionSupport.TryComposeIcon(
            enhancementIconCompositor,
            itemCatalog,
            match.Item,
            parentSet,
            match.Variant,
            boostMetadataProvider);
        return new AccountsBuildEnhancementSlotViewModel(
            iconSource,
            FormatEnhancementTooltip(match.Item.CurrentDisplayName, slot),
            false);
    }

    private static string FormatEnhancementTooltip(
        string displayName,
        HomecomingBuildSlotSnapshot slot)
    {
        var details = new List<string>();
        if (slot.IsAttuned)
        {
            details.Add("Attuned");
        }

        if (slot.BaseEnhancementLevel is int level)
        {
            details.Add($"Level {level}");
        }

        if (slot.BoostValue is int boost)
        {
            details.Add($"+{boost}");
        }

        return details.Count == 0
            ? displayName
            : $"{displayName} — {string.Join(", ", details)}";
    }

    private static IReadOnlyDictionary<string, EnhancementCatalogMatch> BuildEnhancementIndex(
        IItemReferenceCatalog? itemCatalog)
    {
        var index = new Dictionary<string, EnhancementCatalogMatch>(StringComparer.OrdinalIgnoreCase);
        if (itemCatalog is null || !itemCatalog.IsLoaded)
        {
            return index;
        }

        foreach (var item in itemCatalog.GetEnhancements(ReferenceCatalogQueryScope.CurrentHomecoming))
        {
            foreach (var variant in item.SourceVariants)
            {
                var sourceToken = GetSourceToken(variant.HomecomingSourceId);
                if (sourceToken is not null)
                {
                    index.TryAdd(sourceToken, new EnhancementCatalogMatch(item, variant));
                }
            }
        }

        return index;
    }

    private static string? GetSourceToken(string sourceId)
    {
        var separator = sourceId.LastIndexOf('.');
        return separator >= 0 && separator < sourceId.Length - 1
            ? sourceId[(separator + 1)..]
            : null;
    }

    private static SectionBuilder? FindRoleSection(
        IEnumerable<SectionBuilder> sections,
        string? powersetName)
    {
        if (string.IsNullOrWhiteSpace(powersetName))
        {
            return null;
        }

        var normalizedName = NormalizeName(powersetName);
        return sections.FirstOrDefault(section =>
            string.Equals(NormalizeName(section.DisplayName), normalizedName, StringComparison.Ordinal)
            || string.Equals(
                NormalizeName(section.Identity.RawPowerSetToken),
                normalizedName,
                StringComparison.Ordinal));
    }

    private static string NormalizeName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string FormatRawToken(string value) =>
        string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries));

    private sealed class SectionBuilder(PowerSetIdentity identity, string displayName)
    {
        public PowerSetIdentity Identity { get; } = identity;

        public string DisplayName { get; } = displayName;

        public List<AccountsBuildPowerViewModel> Powers { get; } = [];

        public AccountsBuildPowerSetSectionViewModel ToViewModel() =>
            new(DisplayName, Powers.ToArray());
    }

    private readonly record struct PowerSetIdentity(string RawCategoryToken, string RawPowerSetToken);

    private sealed record EnhancementCatalogMatch(
        ItemReferenceRecord Item,
        EnhancementSourceVariantReferenceRecord Variant);
}

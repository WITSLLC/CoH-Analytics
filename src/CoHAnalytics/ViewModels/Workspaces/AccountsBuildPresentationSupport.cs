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
        IHomecomingBoostMetadataProvider? boostMetadataProvider,
        IEnhancementHelpResolver? enhancementHelpResolver = null)
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
                    boostMetadataProvider,
                    enhancementHelpResolver,
                    snapshot.CharacterLevel))
                .ToArray();
            var tooltip = FormatPowerTooltip(
                powerDisplayName,
                resolved ? powerReference.PowerType : HomecomingPowerType.Unknown,
                resolved ? powerReference.DisplayHelp : null,
                sourcePower.AcquisitionLevel);

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
        IReadOnlyDictionary<string, AccountsBuildEnhancementCatalogMatch> enhancementIndex,
        IInstalledGameAssetProvider? assetProvider,
        IItemReferenceCatalog? itemCatalog,
        IEnhancementIconCompositor? enhancementIconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadataProvider,
        IEnhancementHelpResolver? enhancementHelpResolver,
        int? characterLevel)
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
            FormatEnhancementTooltip(
                match.Item.CurrentDisplayName,
                slot,
                match,
                enhancementHelpResolver,
                characterLevel),
            false);
    }

    private static string FormatPowerTooltip(
        string displayName,
        HomecomingPowerType powerType,
        string? displayHelp,
        int acquisitionLevel)
    {
        var lines = new List<string> { displayName };
        if (powerType != HomecomingPowerType.Unknown)
        {
            lines.Add(powerType.ToString());
        }

        if (!string.IsNullOrWhiteSpace(displayHelp))
        {
            lines.Add(string.Empty);
            lines.Add(HomecomingHelpDisplayFormatter.NormalizeForDisplay(displayHelp.Trim()) ?? displayHelp.Trim());
        }

        lines.Add(string.Empty);
        lines.Add($"Taken at Level {acquisitionLevel}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatEnhancementTooltip(
        string displayName,
        HomecomingBuildSlotSnapshot slot,
        AccountsBuildEnhancementCatalogMatch? match = null,
        IEnhancementHelpResolver? enhancementHelpResolver = null,
        int? characterLevel = null)
    {
        var details = new List<string> { displayName };
        if (slot.IsAttuned)
        {
            details.Add(match?.Variant.SourceForm.Contains("Superior", StringComparison.OrdinalIgnoreCase) == true
                ? "Superior Attuned"
                : "Attuned");
        }
        else if (slot.BaseEnhancementLevel is int level)
        {
            var boostLabel = slot.BoostValue is int boost ? $" +{boost}" : string.Empty;
            details.Add($"Level {level}{boostLabel}");
        }

        var help = ResolveEnhancementHelp(match, slot, enhancementHelpResolver, characterLevel);
        if (!string.IsNullOrWhiteSpace(help))
        {
            details.Add(string.Empty);
            details.Add(HomecomingHelpDisplayFormatter.NormalizeForDisplay(help.Trim()) ?? help.Trim());
        }

        return string.Join(Environment.NewLine, details);
    }

    private static string? ResolveEnhancementHelp(
        AccountsBuildEnhancementCatalogMatch? match,
        HomecomingBuildSlotSnapshot slot,
        IEnhancementHelpResolver? enhancementHelpResolver,
        int? characterLevel)
    {
        var template = match?.Variant.DisplayHelp ?? match?.Item.DisplayHelp;
        if (match is null || enhancementHelpResolver is null || string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        var usesPlayerLevel = slot.IsAttuned || match.Variant.BoostUsePlayerLevel;
        var presentationLevel = usesPlayerLevel
            ? characterLevel ?? slot.BaseEnhancementLevel ?? 1
            : Math.Max(1, (slot.BaseEnhancementLevel ?? characterLevel ?? 1) + (slot.BoostValue ?? 0));
        var result = enhancementHelpResolver.Resolve(
            template,
            match.Variant,
            new EnhancementHelpPresentationContext { PresentationLevel = presentationLevel });
        return result.Status is EnhancementHelpResolutionStatus.FullyResolved
            or EnhancementHelpResolutionStatus.Unchanged
                ? result.ResolvedText
                : null;
    }

    public static IReadOnlyDictionary<string, AccountsBuildEnhancementCatalogMatch> BuildEnhancementIndex(
        IItemReferenceCatalog? itemCatalog)
    {
        var index = new Dictionary<string, AccountsBuildEnhancementCatalogMatch>(StringComparer.OrdinalIgnoreCase);
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
                    index.TryAdd(sourceToken, new AccountsBuildEnhancementCatalogMatch(item, variant));
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

}

public sealed record AccountsBuildEnhancementCatalogMatch(
    ItemReferenceRecord Item,
    EnhancementSourceVariantReferenceRecord Variant);

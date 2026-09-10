using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ViewModels.Workspaces;

public enum ReferenceEnhancementBrowseNodeKind
{
    RootSetsBranch = 0,
    RootCommonBranch = 1,
    Category = 2,
    Set = 3,
    CommonGroup = 4,
    Enhancement = 5,
    Level = 6,
    Recipe = 7,
    BadgeExplorationRoot = 8,
    BadgeZone = 9,
    Badge = 10,
    BadgeHistoryPlaquesRoot = 11,
    BadgePlaqueCollection = 12,
    BadgePlaqueZoneSegment = 13,
    HistoryPlaque = 14,
    BadgeAccoladesRoot = 15,
    BadgeAccolade = 16,
    BadgeAccoladeCategory = 17,
    BadgeHistoryPlaquesByBadgeBranch = 18,
    BadgeHistoryPlaquesByZoneBranch = 19,
    BadgeHistoryPlaqueZone = 20
}

public enum ReferenceEnhancementDetailKind
{
    Empty = 0,
    LoadError = 1,
    Branch = 2,
    Category = 3,
    Set = 4,
    Enhancement = 5,
    Recipe = 6,
    Badge = 7,
    HistoryPlaque = 8,
    Accolade = 9
}

public sealed class ReferenceEnhancementBrowseNode
{
    public required ReferenceEnhancementBrowseNodeKind Kind { get; init; }

    public required string DisplayName { get; init; }

    public required string NodeKey { get; init; }

    public string? ParentNodeKey { get; init; }

    public string? SetId { get; init; }

    /// <summary>Canonical Homecoming rarity code when <see cref="Kind"/> is <see cref="ReferenceEnhancementBrowseNodeKind.Set"/>.</summary>
    public string? RarityCode { get; init; }

    public string? EnhancementId { get; init; }

    public string? RecipeId { get; init; }

    public string? CategoryKey { get; init; }

    public string? CommonIoBoostType { get; init; }

    public string? IconIdentity { get; init; }

    /// <summary>Explicit browse level when <see cref="Kind"/> is <see cref="ReferenceEnhancementBrowseNodeKind.Level"/>.</summary>
    public int? PresentationLevel { get; init; }

    public string? BadgeId { get; init; }

    public string? SecondaryLine { get; init; }

    public bool IsAcquired { get; init; }

    public bool IsZoneCompletionBadge { get; init; }

    public bool IsCollectionCompletionBadge { get; init; }

    public string? PlaqueCollectionName { get; init; }

    public string? PlaqueZoneId { get; init; }

    public int? PlaqueLocationIndex { get; init; }

    public IReadOnlyList<ReferenceEnhancementBrowseNode> Children { get; init; } =
        Array.Empty<ReferenceEnhancementBrowseNode>();
}

public enum ReferenceBrowseContentScope
{
    General = 0,
    Invention = 1
}

public sealed class ReferenceEnhancementBrowseBounds
{
    public required int MinimumLevel { get; init; }

    public required int MaximumLevel { get; init; }
}

public sealed class ReferenceEnhancementBrowseFilter
{
    public int MinLevel { get; init; }

    public int MaxLevel { get; init; }

    /// <summary>Null means All rarities.</summary>
    public string? Rarity { get; init; }
}

public sealed class ReferenceEnhancementRarityFilterOption
{
    public required string Label { get; init; }

    public string? Rarity { get; init; }

    /// <summary>Canonical Homecoming rarity code for presentation; null for All.</summary>
    public string? RarityCode { get; init; }
}

public sealed class ReferenceEnhancementBrowseCensus
{
    public int CurrentEnhancementCount { get; init; }

    public int CurrentSetCount { get; init; }

    public int CommonInventionEnhancementCount { get; init; }

    public int CategoryCount { get; init; }

    public int CommonGroupCount { get; init; }
}

public sealed class ReferenceEnhancementBrowseTree
{
    public required IReadOnlyList<ReferenceEnhancementBrowseNode> RootNodes { get; init; }

    public required IReadOnlyDictionary<string, ReferenceEnhancementBrowseNode> NodesByKey { get; init; }

    public required ReferenceEnhancementBrowseCensus Census { get; init; }
}

public sealed class ReferenceEnhancementBranchSummaryItem
{
    public required string Label { get; init; }

    public required int Count { get; init; }

    public required string TargetNodeKey { get; init; }
}

public sealed class ReferenceEnhancementSetSummaryItem
{
    public required string SetId { get; init; }

    public required string DisplayName { get; init; }

    public string IconPlaceholderLetter { get; init; } = "?";

    public string? IconIdentity { get; init; }

    public ImageSource? IconSource { get; init; }
}

public sealed class ReferenceEnhancementMemberSummaryItem
{
    public required string EnhancementId { get; init; }

    public required string TargetNodeKey { get; init; }

    public required string DisplayName { get; init; }

    public string IconPlaceholderLetter { get; init; } = "?";

    public string? IconIdentity { get; init; }

    public ImageSource? IconSource { get; init; }
}

public sealed class ReferenceEnhancementSetBonusTierDisplay
{
    public required string PieceRangeLabel { get; init; }

    public required ReferenceEnhancementSetBonusRequiresPattern RequiresPattern { get; init; }

    public string? ConditionLabel { get; init; }

    public required IReadOnlyList<string> AutoPowerHelps { get; init; }
}

public sealed class ReferenceEnhancementVariantHelpDisplay
{
    public required string SourceFormLabel { get; init; }

    public required string HelpText { get; init; }

    public string? CanonicalHelpText { get; init; }

    public EnhancementHelpResolutionStatus HelpResolutionStatus { get; init; }
}

public sealed class ReferenceEnhancementDetailModel
{
    public ReferenceEnhancementDetailKind Kind { get; init; } = ReferenceEnhancementDetailKind.Empty;

    public string? ErrorMessage { get; init; }

    public string? Title { get; init; }

    public string? Summary { get; init; }

    public string? CategoryLabel { get; init; }

    public string? RarityLabel { get; init; }

    public string? RarityCode { get; init; }

    public string? LevelRangeLabel { get; init; }

    public string? ParentSetName { get; init; }

    public string? ParentSetId { get; init; }

    public string? FamilyLabel { get; init; }

    public string? IconIdentity { get; init; }

    public ImageSource? IconSource { get; init; }

    public string IconPlaceholderLetter { get; init; } = "?";

    public string? LogicalHelp { get; init; }

    public string? CanonicalDisplayHelp { get; init; }

    public string? ResolvedDisplayHelp { get; init; }

    public EnhancementHelpResolutionStatus? HelpResolutionStatus { get; init; }

    public int PresentationLevel { get; init; }

    public string? PresentationLevelLabel { get; init; }

    public bool ShowHelpResolutionNote { get; init; }

    public string HelpResolutionNote { get; init; } =
        "Some values could not be resolved from current Homecoming data.";

    public string? ShortHelp { get; init; }

    public string? AvailableSourceFormsLabel { get; init; }

    public IReadOnlyList<ReferenceEnhancementBranchSummaryItem> BranchItems { get; init; } =
        Array.Empty<ReferenceEnhancementBranchSummaryItem>();

    public IReadOnlyList<ReferenceEnhancementSetSummaryItem> SetItems { get; init; } =
        Array.Empty<ReferenceEnhancementSetSummaryItem>();

    public IReadOnlyList<ReferenceEnhancementMemberSummaryItem> MemberItems { get; init; } =
        Array.Empty<ReferenceEnhancementMemberSummaryItem>();

    public IReadOnlyList<ReferenceEnhancementSetBonusTierDisplay> SetBonusTiers { get; init; } =
        Array.Empty<ReferenceEnhancementSetBonusTierDisplay>();

    public IReadOnlyList<ReferenceEnhancementVariantHelpDisplay> VariantHelpSections { get; init; } =
        Array.Empty<ReferenceEnhancementVariantHelpDisplay>();

    public bool ShowVariantHelpSections { get; init; }

    public string? ProducedEnhancementId { get; init; }

    public string? ProducedEnhancementName { get; init; }

    public string? CraftingCostLabel { get; init; }

    public IReadOnlyList<ReferenceRecipeSalvageRequirementItem> SalvageRequirements { get; init; } =
        Array.Empty<ReferenceRecipeSalvageRequirementItem>();

    public string? AcquiredStateLabel { get; init; }

    public bool ShowAcquiredState { get; init; }

    public string? ZoneLabel { get; init; }

    public bool ShowZone { get; init; }

    public string? ThumbtackCommand { get; init; }

    public bool ShowThumbtack { get; init; }

    public string? LocationNote { get; init; }

    public bool ShowLocationNote { get; init; }

    public string? RewardText { get; init; }

    public bool ShowReward { get; init; }

    public bool UseWideBadgeIcon { get; init; }

    public string? RequirementIntroText { get; init; }

    public bool ShowRequirementIntro { get; init; }

    public bool ShowRequirementLogicNote { get; init; }

    public string? RequirementLogicNote { get; init; }

    public bool ShowAccoladeRequirements { get; init; }

    public IReadOnlyList<ReferenceAccoladeRequirementItemDisplay> AccoladeRequirements { get; init; } =
        Array.Empty<ReferenceAccoladeRequirementItemDisplay>();
}

public sealed class ReferenceAccoladeRequirementItemDisplay
{
    public required string BadgeId { get; init; }

    public required string DisplayName { get; init; }

    public string? AcquiredStateLabel { get; init; }

    public bool ShowAcquiredState { get; init; }

    public string? LogicConnectorAfter { get; init; }
}

public sealed class ReferenceRecipeSalvageRequirementItem
{
    public required string SalvageItemId { get; init; }

    public required string DisplayName { get; init; }

    public required uint Quantity { get; init; }

    public string QuantityLabel => $"{Quantity} ×";

    public string IconPlaceholderLetter { get; init; } = "?";

    public ImageSource? IconSource { get; init; }

    public string? RarityCode { get; init; }
}

public static class ReferenceEnhancementBrowseSupport
{
    public const string SetsRootNodeKey = "branch:sets";

    public const string CommonRootNodeKey = "branch:common";

    public const int DefaultPresentationLevel = 50;

    public const int GeneralDefaultMinimumLevel = 1;

    public const int InventionDefaultMinimumLevel = 10;

    private const string HelpResolutionNoteText =
        "Some values could not be resolved from current Homecoming data.";

    private static readonly string[] ResolutionSourceFormPreference =
    [
        "Crafted",
        "Attuned",
        "Superior_Attuned"
    ];

    private static readonly string[] SourceFormDisplayOrder =
    [
        "Crafted",
        "Attuned",
        "Superior Attuned"
    ];

    public static ReferenceEnhancementBrowseBounds GetBrowseBounds(IItemReferenceCatalog catalog)
    {
        var sets = catalog.GetEnhancementSets(ReferenceCatalogQueryScope.CurrentHomecoming).Values;
        var minimum = sets
            .Where(set => set.MinimumLevel is not null)
            .Select(set => set.MinimumLevel!.Value)
            .DefaultIfEmpty(1)
            .Min();
        var nativeMaximum = sets
            .Where(set => set.MaximumLevel is not null)
            .Select(set => set.MaximumLevel!.Value)
            .DefaultIfEmpty(DefaultPresentationLevel)
            .Max();

        return new ReferenceEnhancementBrowseBounds
        {
            MinimumLevel = minimum,
            MaximumLevel = Math.Max(
                nativeMaximum,
                EnhancementHelpResolverBoosterMultipliers.MaxBoostedPresentationLevel)
        };
    }

    public static IReadOnlyList<ReferenceEnhancementRarityFilterOption> GetRarityFilterOptions(
        IItemReferenceCatalog catalog)
    {
        var rarities = catalog
            .GetEnhancementSets(ReferenceCatalogQueryScope.CurrentHomecoming)
            .Values
            .Where(set => !string.IsNullOrWhiteSpace(set.RarityDisplayText))
            .GroupBy(set => set.RarityDisplayText!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReferenceEnhancementRarityFilterOption
            {
                Label = group.Key,
                Rarity = group.Key,
                RarityCode = group
                    .Select(set => set.RarityCode)
                    .FirstOrDefault(code => !string.IsNullOrWhiteSpace(code))
            })
            .ToArray();

        return
        [
            new ReferenceEnhancementRarityFilterOption { Label = "All", Rarity = null },
            ..rarities
        ];
    }

    public static ReferenceEnhancementBrowseFilter NormalizeFilter(
        ReferenceEnhancementBrowseFilter filter,
        ReferenceEnhancementBrowseBounds bounds)
    {
        var minLevel = Math.Clamp(filter.MinLevel, bounds.MinimumLevel, bounds.MaximumLevel);
        var maxLevel = Math.Clamp(filter.MaxLevel, bounds.MinimumLevel, bounds.MaximumLevel);
        if (minLevel > maxLevel)
        {
            maxLevel = minLevel;
        }

        return new ReferenceEnhancementBrowseFilter
        {
            MinLevel = minLevel,
            MaxLevel = maxLevel,
            Rarity = filter.Rarity
        };
    }

    public static ReferenceEnhancementBrowseFilter CreateDefaultLevelFilter(
        ReferenceBrowseContentScope scope,
        ReferenceEnhancementBrowseBounds bounds) =>
        NormalizeFilter(
            new ReferenceEnhancementBrowseFilter
            {
                MinLevel = scope == ReferenceBrowseContentScope.Invention
                    ? InventionDefaultMinimumLevel
                    : GeneralDefaultMinimumLevel,
                MaxLevel = Math.Min(DefaultPresentationLevel, bounds.MaximumLevel)
            },
            bounds);

    public static ReferenceBrowseContentScope ResolveContentScope(
        ReferenceEnhancementBrowseTree? browseTree,
        string? nodeKey)
    {
        if (browseTree is null || string.IsNullOrWhiteSpace(nodeKey))
        {
            return ReferenceBrowseContentScope.General;
        }

        if (!browseTree.NodesByKey.TryGetValue(nodeKey, out var node))
        {
            return ReferenceBrowseContentScope.General;
        }

        var current = node;
        while (true)
        {
            if (current.Kind == ReferenceEnhancementBrowseNodeKind.RootCommonBranch)
            {
                return ReferenceBrowseContentScope.Invention;
            }

            if (current.Kind == ReferenceEnhancementBrowseNodeKind.RootSetsBranch)
            {
                return ReferenceBrowseContentScope.General;
            }

            if (string.IsNullOrWhiteSpace(current.ParentNodeKey)
                || !browseTree.NodesByKey.TryGetValue(current.ParentNodeKey, out var parent))
            {
                return ReferenceBrowseContentScope.General;
            }

            current = parent;
        }
    }

    public static bool OriginFilterSupportedByCatalogData() => false;

    public static bool ShouldShowEnhancementBrowseFilters(
        ReferenceSectionId activeSection,
        bool isCatalogLoaded) =>
        isCatalogLoaded
        && activeSection is ReferenceSectionId.Enhancements or ReferenceSectionId.Recipes;

    public static EnhancementSourceVariantReferenceRecord? SelectIconVariant(ItemReferenceRecord item) =>
        SelectResolutionVariant(item);

    public static (
        string? ResolvedDisplayHelp,
        string? CanonicalDisplayHelp,
        EnhancementHelpResolutionStatus? Status,
        bool ShowHelpResolutionNote)
        ResolveEnhancementDisplayHelp(
            ItemReferenceRecord enhancement,
            IEnhancementHelpResolver? helpResolver,
            int presentationLevel)
    {
        var variant = SelectResolutionVariant(enhancement);
        var canonical = !string.IsNullOrWhiteSpace(enhancement.DisplayHelp)
            ? enhancement.DisplayHelp
            : variant?.DisplayHelp;
        if (string.IsNullOrWhiteSpace(canonical))
        {
            return (null, null, null, false);
        }

        if (variant is null)
        {
            return (canonical, canonical, null, false);
        }

        var resolved = ResolveVariantHelp(helpResolver, variant, presentationLevel);
        return (
            resolved.DisplayHelp ?? canonical,
            canonical,
            resolved.Status,
            resolved.ShowResolutionNote);
    }

    public static ImageSource? ResolveComposedIconSource(
        IEnhancementIconCompositor? compositor,
        IItemReferenceCatalog catalog,
        ItemReferenceRecord item,
        EnhancementSetReferenceRecord? parentSet,
        IHomecomingBoostMetadataProvider? boostMetadata = null)
    {
        var iconVariant = SelectIconVariant(item);
        return EnhancementIconCompositionSupport.TryComposeIcon(
            compositor,
            catalog,
            item,
            parentSet,
            iconVariant,
            boostMetadata);
    }

    public static ImageSource? ResolveSetComposedIconSource(
        IEnhancementIconCompositor? compositor,
        IItemReferenceCatalog catalog,
        EnhancementSetReferenceRecord set,
        IHomecomingBoostMetadataProvider? boostMetadata = null) =>
        EnhancementIconCompositionSupport.TryComposeSetArtworkIcon(
            compositor,
            catalog,
            set,
            SelectIconVariant,
            boostMetadata);

    public static EnhancementIconCompositionRequest? TryBuildCompositionRequest(
        IItemReferenceCatalog catalog,
        ItemReferenceRecord item,
        EnhancementSetReferenceRecord? parentSet,
        IHomecomingBoostMetadataProvider? boostMetadata = null)
    {
        var iconVariant = SelectIconVariant(item);
        return iconVariant is null
            ? null
            : EnhancementIconCompositionSupport.TryBuildRequest(
                item,
                parentSet,
                iconVariant,
                boostMetadata,
                catalog);
    }

    public static ReferenceEnhancementBrowseTree BuildBrowseTree(
        IItemReferenceCatalog catalog,
        ReferenceEnhancementBrowseFilter? filter = null)
    {
        var bounds = GetBrowseBounds(catalog);
        var normalizedFilter = filter is null
            ? new ReferenceEnhancementBrowseFilter
            {
                MinLevel = bounds.MinimumLevel,
                MaxLevel = bounds.MaximumLevel
            }
            : NormalizeFilter(filter, bounds);

        var enhancements = catalog.GetEnhancements(ReferenceCatalogQueryScope.CurrentHomecoming);
        var sets = catalog.GetEnhancementSets(ReferenceCatalogQueryScope.CurrentHomecoming);
        var enhancementsBySet = enhancements
            .Where(item => !string.IsNullOrWhiteSpace(item.EnhancementSetId))
            .GroupBy(item => item.EnhancementSetId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(item => item.CurrentDisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.Ordinal);

        var filteredSets = sets.Values
            .Where(set => MatchesRarityFilter(set, normalizedFilter.Rarity))
            .ToArray();

        var categoryGroups = filteredSets
            .GroupBy(
                set => ResolveCategoryKey(set),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => ResolveCategoryDisplayName(group.First()), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var categoryNodes = new List<ReferenceEnhancementBrowseNode>(categoryGroups.Length);
        foreach (var categoryGroup in categoryGroups)
        {
            var categoryDisplayName = ResolveCategoryDisplayName(categoryGroup.First());
            var categoryKey = categoryGroup.Key;
            var categoryNodeKey = $"category:{categoryKey}";

            var setNodes = categoryGroup
                .OrderBy(set => set.CurrentDisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(set => CreateSetNode(
                    catalog,
                    set,
                    enhancementsBySet,
                    categoryNodeKey,
                    normalizedFilter))
                .Where(node => node.Children.Count > 0)
                .ToArray();

            if (setNodes.Length == 0)
            {
                continue;
            }

            categoryNodes.Add(new ReferenceEnhancementBrowseNode
            {
                Kind = ReferenceEnhancementBrowseNodeKind.Category,
                DisplayName = categoryDisplayName,
                NodeKey = categoryNodeKey,
                ParentNodeKey = SetsRootNodeKey,
                CategoryKey = categoryKey,
                Children = setNodes
            });
        }

        var setsRoot = new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.RootSetsBranch,
            DisplayName = "Enhancement Sets",
            NodeKey = SetsRootNodeKey,
            Children = categoryNodes
        };

        var commonEnhancements = enhancements
            .Where(item =>
                item.EnhancementFamily == ReferenceEnhancementFamily.CraftedInvention
                && string.IsNullOrWhiteSpace(item.EnhancementSetId))
            .ToArray();

        var commonGroups = commonEnhancements
            .GroupBy(
                item => item.CommonIoBoostType ?? item.CatalogItemId,
                StringComparer.Ordinal)
            .OrderBy(group => ResolveCommonGroupLabel(group.First()), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var commonGroupNodes = new List<ReferenceEnhancementBrowseNode>(commonGroups.Length);
        foreach (var commonGroup in commonGroups)
        {
            var groupLabel = ResolveCommonGroupLabel(commonGroup.First());
            var groupKey = commonGroup.Key;
            var groupNodeKey = $"common-group:{groupKey}";

            var enhancementNodes = commonGroup
                .Select(item => CreateEnhancementNode(catalog, item, groupNodeKey, parentSet: null, normalizedFilter))
                .Where(node => node is not null)
                .Cast<ReferenceEnhancementBrowseNode>()
                .OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (enhancementNodes.Length == 0)
            {
                continue;
            }

            commonGroupNodes.Add(new ReferenceEnhancementBrowseNode
            {
                Kind = ReferenceEnhancementBrowseNodeKind.CommonGroup,
                DisplayName = groupLabel,
                NodeKey = groupNodeKey,
                ParentNodeKey = CommonRootNodeKey,
                CommonIoBoostType = groupKey,
                Children = enhancementNodes
            });
        }

        var commonRoot = new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.RootCommonBranch,
            DisplayName = "Common Invention Enhancements",
            NodeKey = CommonRootNodeKey,
            Children = commonGroupNodes
        };

        var rootNodes = new List<ReferenceEnhancementBrowseNode>(2);
        if (categoryNodes.Count > 0)
        {
            rootNodes.Add(setsRoot);
        }

        if (commonGroupNodes.Count > 0 && normalizedFilter.Rarity is null)
        {
            rootNodes.Add(commonRoot);
        }

        var nodesByKey = IndexNodes(rootNodes);

        return new ReferenceEnhancementBrowseTree
        {
            RootNodes = rootNodes,
            NodesByKey = nodesByKey,
            Census = new ReferenceEnhancementBrowseCensus
            {
                CurrentEnhancementCount = enhancements.Count,
                CurrentSetCount = sets.Count,
                CommonInventionEnhancementCount = commonEnhancements.Length,
                CategoryCount = categoryNodes.Count,
                CommonGroupCount = commonGroupNodes.Count
            }
        };
    }

    public static ReferenceEnhancementDetailModel BuildDetail(
        IItemReferenceCatalog catalog,
        ReferenceEnhancementBrowseNode? selectedNode,
        ReferenceEnhancementBrowseTree browseTree,
        IEnhancementHelpResolver? helpResolver = null,
        int? presentationLevel = null,
        IEnhancementIconCompositor? iconCompositor = null,
        IHomecomingBoostMetadataProvider? boostMetadata = null)
    {
        if (!catalog.IsLoaded)
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.LoadError,
                Title = "Reference unavailable",
                ErrorMessage = catalog.LoadFailureReason ?? "The reference catalog failed to load."
            };
        }

        if (selectedNode is null)
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Empty,
                Title = "Enhancements",
                Summary = "Select an Enhancement Set or Enhancement to view details."
            };
        }

        return selectedNode.Kind switch
        {
            ReferenceEnhancementBrowseNodeKind.RootSetsBranch =>
                BuildBranchDetail(
                    "Enhancement Sets",
                    "Browse Homecoming Enhancement Sets grouped by category.",
                    selectedNode.Children.Select(child => new ReferenceEnhancementBranchSummaryItem
                    {
                        Label = child.DisplayName,
                        Count = child.Children.Count,
                        TargetNodeKey = child.NodeKey
                    }).ToArray()),
            ReferenceEnhancementBrowseNodeKind.RootCommonBranch =>
                BuildBranchDetail(
                    "Common Invention Enhancements",
                    "Browse crafted invention Enhancements grouped by structural type.",
                    selectedNode.Children.Select(child => new ReferenceEnhancementBranchSummaryItem
                    {
                        Label = child.DisplayName,
                        Count = child.Children.Count,
                        TargetNodeKey = child.NodeKey
                    }).ToArray()),
            ReferenceEnhancementBrowseNodeKind.Category =>
                BuildCategoryDetail(selectedNode),
            ReferenceEnhancementBrowseNodeKind.Set =>
                BuildSetDetail(catalog, selectedNode, helpResolver, presentationLevel ?? DefaultPresentationLevel, iconCompositor, boostMetadata),
            ReferenceEnhancementBrowseNodeKind.CommonGroup =>
                BuildCommonGroupDetail(catalog, selectedNode, iconCompositor, boostMetadata),
            ReferenceEnhancementBrowseNodeKind.Enhancement =>
                BuildEnhancementDetail(catalog, selectedNode, helpResolver, presentationLevel, iconCompositor, boostMetadata),
            ReferenceEnhancementBrowseNodeKind.Level =>
                BuildLevelDetail(catalog, selectedNode, helpResolver, iconCompositor, boostMetadata),
            _ => new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Empty,
                Summary = "Select an Enhancement Set or Enhancement to view details."
            }
        };
    }

    public static string FormatSourceForm(string sourceForm) =>
        sourceForm switch
        {
            "Crafted" => "Crafted",
            "Attuned" => "Attuned",
            "Superior_Attuned" => "Superior Attuned",
            _ => sourceForm.Replace('_', ' ')
        };

    public static string FormatPieceRange(int minimumBoosts, int maximumBoosts)
    {
        if (minimumBoosts == maximumBoosts)
        {
            return minimumBoosts == 1
                ? "1 piece"
                : $"{minimumBoosts} pieces";
        }

        return $"{minimumBoosts}–{maximumBoosts} pieces";
    }

    public static IReadOnlyList<ReferenceEnhancementSetBonusTierDisplay> BuildSetBonusTiers(
        IItemReferenceCatalog catalog,
        EnhancementSetReferenceRecord set,
        IEnhancementHelpResolver? helpResolver = null,
        int presentationLevel = DefaultPresentationLevel)
    {
        return set.Bonuses
            .Select(bonus => new ReferenceEnhancementSetBonusTierDisplay
            {
                PieceRangeLabel = FormatPieceRange(bonus.MinimumBoosts, bonus.MaximumBoosts),
                RequiresPattern = bonus.RequiresPattern,
                ConditionLabel = BuildBonusConditionLabel(catalog, bonus),
                AutoPowerHelps = bonus.AutoPowers
                    .Select(power => ResolveSetBonusHelp(helpResolver, power, presentationLevel))
                    .Where(help => !string.IsNullOrWhiteSpace(help))
                    .Cast<string>()
                    .ToArray()
            })
            .ToArray();
    }

    public static IEnumerable<string> ExpandAncestorNodeKeys(string nodeKey, IReadOnlyDictionary<string, ReferenceEnhancementBrowseNode> nodesByKey)
    {
        var currentKey = nodeKey;
        while (nodesByKey.TryGetValue(currentKey, out var node)
            && !string.IsNullOrWhiteSpace(node.ParentNodeKey))
        {
            yield return node.ParentNodeKey!;
            currentKey = node.ParentNodeKey!;
        }
    }

    public static string? FormatAvailableSourceForms(
        IReadOnlyList<EnhancementSourceVariantReferenceRecord> sourceVariants)
    {
        if (sourceVariants.Count == 0)
        {
            return null;
        }

        var labels = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preferred in SourceFormDisplayOrder)
        {
            if (sourceVariants.Any(variant =>
                    string.Equals(FormatSourceForm(variant.SourceForm), preferred, StringComparison.Ordinal))
                && seen.Add(preferred))
            {
                labels.Add(preferred);
            }
        }

        foreach (var variant in sourceVariants
                     .Select(variant => FormatSourceForm(variant.SourceForm))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(label => label, StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Add(variant))
            {
                labels.Add(variant);
            }
        }

        return labels.Count == 0 ? null : string.Join(" · ", labels);
    }

    public static bool RequiresLevelSelection(ItemReferenceRecord item) =>
        ContainsScaleTokens(item.DisplayHelp)
        || item.SourceVariants.Any(variant => ContainsScaleTokens(variant.DisplayHelp));

    public static IReadOnlyList<int> GetSelectableLevels(
        ItemReferenceRecord item,
        EnhancementSetReferenceRecord? parentSet)
    {
        if (!RequiresLevelSelection(item))
        {
            return Array.Empty<int>();
        }

        IReadOnlyList<int> nativeLevels;
        if (parentSet?.MinimumLevel is int minimumLevel
            && parentSet.MaximumLevel is int maximumLevel)
        {
            nativeLevels = Enumerable.Range(minimumLevel, maximumLevel - minimumLevel + 1).ToArray();
        }
        else
        {
            var suffixLevels = item.SourceVariants
                .Select(variant => TryParseLevelSuffix(variant.HomecomingSourceId))
                .Where(level => level is not null)
                .Select(level => level!.Value)
                .Distinct()
                .OrderBy(level => level)
                .ToArray();

            if (suffixLevels.Length > 0)
            {
                nativeLevels = suffixLevels;
            }
            else
            {
                var maxBoostLevel = SelectResolutionVariant(item)?.MaxBoostLevel ?? DefaultPresentationLevel;
                nativeLevels = maxBoostLevel <= 0
                    ? Array.Empty<int>()
                    : Enumerable.Range(1, maxBoostLevel).ToArray();
            }
        }

        return AppendBoostedPresentationLevels(item, nativeLevels);
    }

    internal static IReadOnlyList<int> GetBoostedPresentationLevels(ItemReferenceRecord item)
    {
        var variant = SelectResolutionVariant(item);
        if (variant is null
            || !EnhancementHelpResolverBoosterMultipliers.SupportsBoostedPresentationLevels(variant))
        {
            return Array.Empty<int>();
        }

        return Enumerable.Range(
            EnhancementHelpResolverBoosterMultipliers.NativeCapPresentationLevel + 1,
            EnhancementHelpResolverBoosterMultipliers.MaxBoostedPresentationLevel
                - EnhancementHelpResolverBoosterMultipliers.NativeCapPresentationLevel)
            .ToArray();
    }

    private static IReadOnlyList<int> AppendBoostedPresentationLevels(
        ItemReferenceRecord item,
        IReadOnlyList<int> nativeLevels)
    {
        var boostedLevels = GetBoostedPresentationLevels(item);
        if (boostedLevels.Count == 0)
        {
            return nativeLevels;
        }

        if (nativeLevels.Count == 0)
        {
            return boostedLevels;
        }

        var merged = nativeLevels.ToList();
        foreach (var level in boostedLevels)
        {
            if (!merged.Contains(level))
            {
                merged.Add(level);
            }
        }

        return merged;
    }

    public static IReadOnlyList<int> GetVisibleSelectableLevels(
        ItemReferenceRecord item,
        EnhancementSetReferenceRecord? parentSet,
        ReferenceEnhancementBrowseFilter filter) =>
        GetSelectableLevels(item, parentSet)
            .Where(level => level >= filter.MinLevel && level <= filter.MaxLevel)
            .ToArray();

    public static bool EnhancementMatchesLevelFilter(
        ItemReferenceRecord item,
        EnhancementSetReferenceRecord? parentSet,
        ReferenceEnhancementBrowseFilter filter)
    {
        if (!RequiresLevelSelection(item))
        {
            return true;
        }

        return GetVisibleSelectableLevels(item, parentSet, filter).Count > 0;
    }

    public static ReferenceEnhancementBrowseNode? FindNodeForEnhancementLevel(
        ReferenceEnhancementBrowseTree browseTree,
        string enhancementId,
        int presentationLevel) =>
        browseTree.NodesByKey.TryGetValue(
            CreateLevelNodeKey(enhancementId, presentationLevel),
            out var node)
            ? node
            : null;

    public static ReferenceEnhancementBrowseNode? FindNodeForEnhancementId(
        ReferenceEnhancementBrowseTree browseTree,
        string enhancementId) =>
        browseTree.NodesByKey.TryGetValue($"enh:{enhancementId}", out var node) ? node : null;

    public static string CreateLevelNodeKey(string enhancementId, int presentationLevel) =>
        $"enh:{enhancementId}:lvl:{presentationLevel}";

    public static string? TryResolveReplacementNodeKey(
        ReferenceEnhancementBrowseTree browseTree,
        string? previousNodeKey)
    {
        if (string.IsNullOrWhiteSpace(previousNodeKey))
        {
            return null;
        }

        if (browseTree.NodesByKey.ContainsKey(previousNodeKey))
        {
            return previousNodeKey;
        }

        if (!TryParseLevelNodeKey(previousNodeKey, out var enhancementId, out var previousLevel))
        {
            return null;
        }

        var enhancementNodeKey = $"enh:{enhancementId}";
        if (browseTree.NodesByKey.TryGetValue(enhancementNodeKey, out var enhancementNode))
        {
            var replacementLevel = enhancementNode.Children
                .Where(child => child.Kind == ReferenceEnhancementBrowseNodeKind.Level)
                .Select(child => child.PresentationLevel!.Value)
                .OrderBy(level => Math.Abs(level - previousLevel))
                .ThenBy(level => level)
                .Cast<int?>()
                .FirstOrDefault();

            if (replacementLevel is not null)
            {
                return CreateLevelNodeKey(enhancementId, replacementLevel.Value);
            }

            return enhancementNodeKey;
        }

        return null;
    }

    public static ReferenceEnhancementBrowseNode? FindNodeForSetId(
        ReferenceEnhancementBrowseTree browseTree,
        string setId) =>
        browseTree.NodesByKey.TryGetValue($"set:{setId}", out var node) ? node : null;

    private static ReferenceEnhancementBrowseNode CreateSetNode(
        IItemReferenceCatalog catalog,
        EnhancementSetReferenceRecord set,
        IReadOnlyDictionary<string, ItemReferenceRecord[]> enhancementsBySet,
        string categoryNodeKey,
        ReferenceEnhancementBrowseFilter filter)
    {
        var setNodeKey = $"set:{set.CatalogItemId}";
        enhancementsBySet.TryGetValue(set.CatalogItemId, out var members);
        members ??= Array.Empty<ItemReferenceRecord>();

        var memberNodes = members
            .Select(item => CreateEnhancementNode(catalog, item, setNodeKey, set, filter))
            .Where(node => node is not null)
            .Cast<ReferenceEnhancementBrowseNode>()
            .ToArray();

        return new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.Set,
            DisplayName = set.CurrentDisplayName,
            NodeKey = setNodeKey,
            ParentNodeKey = categoryNodeKey,
            SetId = set.CatalogItemId,
            RarityCode = set.RarityCode,
            CategoryKey = ResolveCategoryKey(set),
            IconIdentity = null,
            Children = memberNodes
        };
    }

    private static ReferenceEnhancementBrowseNode? CreateEnhancementNode(
        IItemReferenceCatalog catalog,
        ItemReferenceRecord item,
        string parentNodeKey,
        EnhancementSetReferenceRecord? parentSet,
        ReferenceEnhancementBrowseFilter filter)
    {
        if (!EnhancementMatchesLevelFilter(item, parentSet, filter))
        {
            return null;
        }

        var enhancementNodeKey = $"enh:{item.CatalogItemId}";
        var levelNodes = GetVisibleSelectableLevels(item, parentSet, filter)
            .Select(level => new ReferenceEnhancementBrowseNode
            {
                Kind = ReferenceEnhancementBrowseNodeKind.Level,
                DisplayName = $"Level {level}",
                NodeKey = CreateLevelNodeKey(item.CatalogItemId, level),
                ParentNodeKey = enhancementNodeKey,
                EnhancementId = item.CatalogItemId,
                SetId = parentSet?.CatalogItemId,
                PresentationLevel = level
            })
            .ToArray();

        return new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.Enhancement,
            DisplayName = item.CurrentDisplayName,
            NodeKey = enhancementNodeKey,
            ParentNodeKey = parentNodeKey,
            EnhancementId = item.CatalogItemId,
            SetId = parentSet?.CatalogItemId,
            CommonIoBoostType = item.CommonIoBoostType,
            IconIdentity = item.Icon,
            Children = levelNodes
        };
    }

    private static ReferenceEnhancementDetailModel BuildLevelDetail(
        IItemReferenceCatalog catalog,
        ReferenceEnhancementBrowseNode levelNode,
        IEnhancementHelpResolver? helpResolver,
        IEnhancementIconCompositor? iconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadata)
    {
        if (!catalog.TryGetById(levelNode.EnhancementId!, out var item))
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Empty,
                Summary = "Select an Enhancement Set or Enhancement to view details."
            };
        }

        var enhancementNode = new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.Enhancement,
            DisplayName = item.CurrentDisplayName,
            NodeKey = $"enh:{item.CatalogItemId}",
            EnhancementId = item.CatalogItemId,
            SetId = levelNode.SetId,
            IconIdentity = item.Icon
        };

        return BuildEnhancementDetail(
            catalog,
            enhancementNode,
            helpResolver,
            levelNode.PresentationLevel,
            iconCompositor,
            boostMetadata);
    }

    private static ReferenceEnhancementDetailModel BuildBranchDetail(
        string title,
        string summary,
        IReadOnlyList<ReferenceEnhancementBranchSummaryItem> items) =>
        new()
        {
            Kind = ReferenceEnhancementDetailKind.Branch,
            Title = title,
            Summary = summary,
            IconPlaceholderLetter = CreatePlaceholderLetter(title),
            BranchItems = items
        };

    private static ReferenceEnhancementDetailModel BuildCategoryDetail(ReferenceEnhancementBrowseNode categoryNode) =>
        new()
        {
            Kind = ReferenceEnhancementDetailKind.Category,
            Title = categoryNode.DisplayName,
            Summary = $"{categoryNode.Children.Count} Enhancement Sets",
            IconPlaceholderLetter = CreatePlaceholderLetter(categoryNode.DisplayName),
            SetItems = categoryNode.Children
                .Select(child => new ReferenceEnhancementSetSummaryItem
                {
                    SetId = child.SetId!,
                    DisplayName = child.DisplayName,
                    IconPlaceholderLetter = CreatePlaceholderLetter(child.DisplayName)
                })
                .ToArray()
        };

    private static ReferenceEnhancementDetailModel BuildCommonGroupDetail(
        IItemReferenceCatalog catalog,
        ReferenceEnhancementBrowseNode groupNode,
        IEnhancementIconCompositor? iconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadata) =>
        new()
        {
            Kind = ReferenceEnhancementDetailKind.Category,
            Title = groupNode.DisplayName,
            Summary = $"{groupNode.Children.Count} Enhancements",
            IconPlaceholderLetter = CreatePlaceholderLetter(groupNode.DisplayName),
            MemberItems = groupNode.Children
                .Select(child =>
                {
                    catalog.TryGetById(child.EnhancementId!, out var item);
                    return new ReferenceEnhancementMemberSummaryItem
                    {
                        EnhancementId = child.EnhancementId!,
                        TargetNodeKey = child.NodeKey,
                        DisplayName = child.DisplayName,
                        IconIdentity = child.IconIdentity,
                        IconSource = item is null
                            ? null
                            : ResolveComposedIconSource(iconCompositor, catalog, item, null, boostMetadata),
                        IconPlaceholderLetter = CreatePlaceholderLetter(child.DisplayName)
                    };
                })
                .ToArray()
        };

    private static ReferenceEnhancementDetailModel BuildSetDetail(
        IItemReferenceCatalog catalog,
        ReferenceEnhancementBrowseNode setNode,
        IEnhancementHelpResolver? helpResolver,
        int presentationLevel,
        IEnhancementIconCompositor? iconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadata)
    {
        if (!catalog.TryGetEnhancementSetById(setNode.SetId!, out var set))
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Empty,
                Summary = "Select an Enhancement Set or Enhancement to view details."
            };
        }

        return new ReferenceEnhancementDetailModel
        {
            Kind = ReferenceEnhancementDetailKind.Set,
            Title = set.CurrentDisplayName,
            IconPlaceholderLetter = CreatePlaceholderLetter(set.CurrentDisplayName),
            IconSource = ResolveSetComposedIconSource(iconCompositor, catalog, set, boostMetadata),
            CategoryLabel = ResolveCategoryDisplayName(set),
            RarityLabel = set.RarityDisplayText,
            RarityCode = set.RarityCode,
            LevelRangeLabel = FormatLevelRange(set.MinimumLevel, set.MaximumLevel),
            MemberItems = setNode.Children
                .Select(child =>
                {
                    catalog.TryGetById(child.EnhancementId!, out var item);
                    return new ReferenceEnhancementMemberSummaryItem
                    {
                        EnhancementId = child.EnhancementId!,
                        TargetNodeKey = child.NodeKey,
                        DisplayName = child.DisplayName,
                        IconIdentity = child.IconIdentity,
                        IconSource = item is null
                            ? null
                            : ResolveComposedIconSource(iconCompositor, catalog, item, set, boostMetadata),
                        IconPlaceholderLetter = CreatePlaceholderLetter(child.DisplayName)
                    };
                })
                .ToArray(),
            SetBonusTiers = BuildSetBonusTiers(catalog, set, helpResolver, presentationLevel)
        };
    }

    private static ReferenceEnhancementDetailModel BuildEnhancementDetail(
        IItemReferenceCatalog catalog,
        ReferenceEnhancementBrowseNode enhancementNode,
        IEnhancementHelpResolver? helpResolver,
        int? presentationLevel,
        IEnhancementIconCompositor? iconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadata)
    {
        if (!catalog.TryGetById(enhancementNode.EnhancementId!, out var item))
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Empty,
                Summary = "Select an Enhancement Set or Enhancement to view details."
            };
        }

        EnhancementSetReferenceRecord? parentSet = null;
        if (!string.IsNullOrWhiteSpace(item.EnhancementSetId)
            && catalog.TryGetEnhancementSetById(item.EnhancementSetId, out var resolvedSet))
        {
            parentSet = resolvedSet;
        }

        var requiresLevelSelection = RequiresLevelSelection(item);
        var effectivePresentationLevel = requiresLevelSelection
            ? presentationLevel
            : presentationLevel ?? DefaultPresentationLevel;
        var categoryLabel = parentSet is not null
            ? ResolveCategoryDisplayName(parentSet)
            : ResolveCommonGroupLabel(item);

        var variantHelpSections = item.SourceVariants
            .Where(variant => !string.IsNullOrWhiteSpace(variant.DisplayHelp))
            .Select(variant =>
            {
                var resolved = effectivePresentationLevel is null
                    ? new ReferenceEnhancementResolvedHelp(
                        null,
                        EnhancementHelpResolutionStatus.Unchanged,
                        false)
                    : ResolveVariantHelp(helpResolver, variant, effectivePresentationLevel.Value);
                return new ReferenceEnhancementVariantHelpDisplay
                {
                    SourceFormLabel = FormatSourceForm(variant.SourceForm),
                    HelpText = resolved.DisplayHelp ?? variant.DisplayHelp!,
                    CanonicalHelpText = variant.DisplayHelp,
                    HelpResolutionStatus = resolved.Status
                };
            })
            .ToArray();

        var showVariantHelp = string.IsNullOrWhiteSpace(item.DisplayHelp) && variantHelpSections.Length > 0;
        var presentationLevelLabel = effectivePresentationLevel is null
            ? null
            : FormatPresentationLevelLabel(effectivePresentationLevel.Value);
        string? resolvedLogicalHelp = null;
        string? canonicalLogicalHelp = null;
        EnhancementHelpResolutionStatus? logicalStatus = null;
        var showHelpResolutionNote = false;

        if (!showVariantHelp && !string.IsNullOrWhiteSpace(item.DisplayHelp))
        {
            canonicalLogicalHelp = item.DisplayHelp;
            if (requiresLevelSelection && effectivePresentationLevel is null)
            {
                resolvedLogicalHelp = null;
            }
            else
            {
                var resolutionVariant = SelectResolutionVariant(item);
                if (resolutionVariant is not null && effectivePresentationLevel is not null)
                {
                    var resolved = ResolveVariantHelp(
                        helpResolver,
                        resolutionVariant,
                        effectivePresentationLevel.Value);
                    resolvedLogicalHelp = resolved.DisplayHelp ?? item.DisplayHelp;
                    logicalStatus = resolved.Status;
                    showHelpResolutionNote = resolved.ShowResolutionNote;
                }
                else
                {
                    resolvedLogicalHelp = item.DisplayHelp;
                }
            }
        }

        if (showVariantHelp)
        {
            showHelpResolutionNote = variantHelpSections.Any(section =>
                section.HelpResolutionStatus is EnhancementHelpResolutionStatus.PartiallyResolved
                    or EnhancementHelpResolutionStatus.InvalidInput);
        }

        var setBonusPresentationLevel = effectivePresentationLevel ?? DefaultPresentationLevel;
        var iconIdentity = item.Icon ?? item.SourceVariants.FirstOrDefault()?.Icon;

        return new ReferenceEnhancementDetailModel
        {
            Kind = ReferenceEnhancementDetailKind.Enhancement,
            Title = item.CurrentDisplayName,
            IconPlaceholderLetter = CreatePlaceholderLetter(item.CurrentDisplayName),
            IconIdentity = iconIdentity,
            IconSource = ResolveComposedIconSource(iconCompositor, catalog, item, parentSet, boostMetadata),
            CategoryLabel = categoryLabel,
            RarityLabel = parentSet?.RarityDisplayText,
            RarityCode = parentSet?.RarityCode,
            LevelRangeLabel = parentSet is not null
                ? FormatLevelRange(parentSet.MinimumLevel, parentSet.MaximumLevel)
                : null,
            ParentSetName = parentSet?.CurrentDisplayName,
            ParentSetId = parentSet?.CatalogItemId,
            FamilyLabel = item.EnhancementFamily?.ToString(),
            LogicalHelp = resolvedLogicalHelp,
            CanonicalDisplayHelp = canonicalLogicalHelp,
            ResolvedDisplayHelp = resolvedLogicalHelp,
            HelpResolutionStatus = logicalStatus,
            PresentationLevel = effectivePresentationLevel ?? 0,
            PresentationLevelLabel = presentationLevelLabel,
            ShowHelpResolutionNote = showHelpResolutionNote,
            HelpResolutionNote = HelpResolutionNoteText,
            ShortHelp = item.ShortHelp,
            AvailableSourceFormsLabel = FormatAvailableSourceForms(item.SourceVariants),
            ShowVariantHelpSections = showVariantHelp,
            VariantHelpSections = variantHelpSections,
            SetBonusTiers = parentSet is not null
                ? BuildSetBonusTiers(catalog, parentSet, helpResolver, setBonusPresentationLevel)
                : Array.Empty<ReferenceEnhancementSetBonusTierDisplay>()
        };
    }

    private static EnhancementSourceVariantReferenceRecord? SelectResolutionVariant(ItemReferenceRecord item)
    {
        foreach (var sourceForm in ResolutionSourceFormPreference)
        {
            var match = item.SourceVariants.FirstOrDefault(variant =>
                string.Equals(variant.SourceForm, sourceForm, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }

        return item.SourceVariants.FirstOrDefault();
    }

    private static ReferenceEnhancementResolvedHelp ResolveVariantHelp(
        IEnhancementHelpResolver? helpResolver,
        EnhancementSourceVariantReferenceRecord variant,
        int presentationLevel)
    {
        var template = variant.DisplayHelp;
        if (string.IsNullOrWhiteSpace(template))
        {
            return new ReferenceEnhancementResolvedHelp(null, EnhancementHelpResolutionStatus.Unchanged, false);
        }

        if (helpResolver is null)
        {
            return new ReferenceEnhancementResolvedHelp(template, EnhancementHelpResolutionStatus.Unchanged, false);
        }

        var result = helpResolver.Resolve(
            template,
            variant,
            new EnhancementHelpPresentationContext { PresentationLevel = presentationLevel });

        return new ReferenceEnhancementResolvedHelp(
            result.ResolvedText,
            result.Status,
            ShouldShowResolutionNote(result.Status));
    }

    private static string? ResolveSetBonusHelp(
        IEnhancementHelpResolver? helpResolver,
        EnhancementSetBonusPowerReferenceRecord power,
        int presentationLevel)
    {
        if (string.IsNullOrWhiteSpace(power.DisplayHelp))
        {
            return null;
        }

        if (helpResolver is null)
        {
            return power.DisplayHelp;
        }

        var variant = new EnhancementSourceVariantReferenceRecord
        {
            HomecomingSourceId = power.HomecomingSourceId,
            SourceForm = "Set_Bonus",
            DisplayHelp = power.DisplayHelp,
            BoostUsePlayerLevel = power.BoostUsePlayerLevel,
            MaxBoostLevel = power.MaxBoostLevel,
            BoostBoostable = power.BoostBoostable,
            Effects = power.Effects
        };

        return ResolveVariantHelp(helpResolver, variant, presentationLevel).DisplayHelp;
    }

    private static bool ShouldShowResolutionNote(EnhancementHelpResolutionStatus status) =>
        status is EnhancementHelpResolutionStatus.PartiallyResolved
            or EnhancementHelpResolutionStatus.InvalidInput;

    private static string FormatPresentationLevelLabel(int presentationLevel) =>
        $"Values shown for Level {presentationLevel}";

    private static bool MatchesRarityFilter(EnhancementSetReferenceRecord set, string? rarityFilter)
    {
        if (rarityFilter is null)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(set.RarityDisplayText)
            && string.Equals(set.RarityDisplayText, rarityFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsScaleTokens(string? help) =>
        !string.IsNullOrWhiteSpace(help)
        && help.Contains("{Boost.Attrib.", StringComparison.Ordinal);

    private static int? TryParseLevelSuffix(string homecomingSourceId)
    {
        var lastSegment = homecomingSourceId.Split('.').LastOrDefault();
        if (string.IsNullOrWhiteSpace(lastSegment))
        {
            return null;
        }

        var separatorIndex = lastSegment.LastIndexOf('_');
        if (separatorIndex < 0 || separatorIndex == lastSegment.Length - 1)
        {
            return null;
        }

        return int.TryParse(lastSegment.AsSpan(separatorIndex + 1), out var level) && level >= 1
            ? level
            : null;
    }

    private static bool TryParseLevelNodeKey(
        string nodeKey,
        out string enhancementId,
        out int presentationLevel)
    {
        enhancementId = string.Empty;
        presentationLevel = 0;
        const string prefix = "enh:";
        const string levelMarker = ":lvl:";
        if (!nodeKey.StartsWith(prefix, StringComparison.Ordinal)
            || !nodeKey.Contains(levelMarker, StringComparison.Ordinal))
        {
            return false;
        }

        var levelIndex = nodeKey.LastIndexOf(levelMarker, StringComparison.Ordinal);
        enhancementId = nodeKey[prefix.Length..levelIndex];
        return int.TryParse(
            nodeKey.AsSpan(levelIndex + levelMarker.Length),
            out presentationLevel);
    }

    private sealed record ReferenceEnhancementResolvedHelp(
        string? DisplayHelp,
        EnhancementHelpResolutionStatus Status,
        bool ShowResolutionNote);

    private static string? BuildBonusConditionLabel(
        IItemReferenceCatalog catalog,
        EnhancementSetBonusReferenceRecord bonus)
    {
        return bonus.RequiresPattern switch
        {
            ReferenceEnhancementSetBonusRequiresPattern.None => null,
            ReferenceEnhancementSetBonusRequiresPattern.PvPMap => "PvP only",
            ReferenceEnhancementSetBonusRequiresPattern.PieceGate =>
                bonus.RequiredEnhancementIds.Count == 0
                    ? "Requires specific Enhancement"
                    : "Requires " + string.Join(
                        ", ",
                        bonus.RequiredEnhancementIds
                            .Select(id => catalog.TryGetById(id, out var item)
                                ? item.CurrentDisplayName
                                : "specific Enhancement")),
            ReferenceEnhancementSetBonusRequiresPattern.Other => null,
            _ => null
        };
    }

    private static string ResolveCategoryKey(EnhancementSetReferenceRecord set) =>
        set.CategoryDisplayText
        ?? set.CategoryCode
        ?? "Uncategorized";

    private static string ResolveCategoryDisplayName(EnhancementSetReferenceRecord set) =>
        set.CategoryDisplayText
        ?? set.CategoryCode
        ?? "Uncategorized";

    private static string ResolveCommonGroupLabel(ItemReferenceRecord item) =>
        item.CommonIoBoostTypeDisplayText
        ?? item.CommonIoBoostType
        ?? item.CurrentDisplayName;

    private static string CreatePlaceholderLetter(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "?" : char.ToUpperInvariant(text[0]).ToString();

    private static string? FormatLevelRange(int? minimumLevel, int? maximumLevel)
    {
        if (minimumLevel is null && maximumLevel is null)
        {
            return null;
        }

        if (minimumLevel is not null && maximumLevel is not null)
        {
            return minimumLevel == maximumLevel
                ? $"Level {minimumLevel}"
                : $"Levels {minimumLevel}–{maximumLevel}";
        }

        return minimumLevel is not null
            ? $"Level {minimumLevel}+"
            : $"Up to level {maximumLevel}";
    }

    private static Dictionary<string, ReferenceEnhancementBrowseNode> IndexNodes(
        IReadOnlyList<ReferenceEnhancementBrowseNode> rootNodes)
    {
        var nodesByKey = new Dictionary<string, ReferenceEnhancementBrowseNode>(StringComparer.Ordinal);
        var stack = new Stack<ReferenceEnhancementBrowseNode>(rootNodes.Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            nodesByKey[node.NodeKey] = node;
            foreach (var child in node.Children.Reverse())
            {
                stack.Push(child);
            }
        }

        return nodesByKey;
    }
}

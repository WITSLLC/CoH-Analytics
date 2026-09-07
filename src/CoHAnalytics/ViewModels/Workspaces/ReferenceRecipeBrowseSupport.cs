using System.Globalization;
using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ViewModels.Workspaces;

public static class ReferenceRecipeBrowseSupport
{
    public const string SetsRootNodeKey = "branch:recipe-sets";

    public const string CommonRootNodeKey = "branch:recipe-common";

    public const int MaxReferenceLevel = 50;

    public static ReferenceEnhancementBrowseBounds GetBrowseBounds(IItemReferenceCatalog catalog)
    {
        var levels = catalog.GetRecipes()
            .SelectMany(recipe => recipe.ValidLevels)
            .Where(level => level is >= 1 and <= MaxReferenceLevel)
            .ToArray();

        return new ReferenceEnhancementBrowseBounds
        {
            MinimumLevel = levels.Length == 0 ? 1 : levels.Min(),
            MaximumLevel = levels.Length == 0 ? MaxReferenceLevel : Math.Min(MaxReferenceLevel, levels.Max())
        };
    }

    public static IReadOnlyList<ReferenceEnhancementRarityFilterOption> GetRarityFilterOptions(
        IItemReferenceCatalog catalog)
    {
        var rarities = catalog.GetRecipes()
            .Select(recipe => recipe.Rarity)
            .Where(rarity => !string.IsNullOrWhiteSpace(rarity))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(rarity => RaritySortKey(rarity), StringComparer.Ordinal)
            .ThenBy(rarity => rarity, StringComparer.OrdinalIgnoreCase)
            .Select(rarity => new ReferenceEnhancementRarityFilterOption
            {
                Label = rarity!,
                Rarity = rarity,
                RarityCode = ToRarityCode(rarity)
            })
            .ToArray();

        return
        [
            new ReferenceEnhancementRarityFilterOption { Label = "All", Rarity = null },
            ..rarities
        ];
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
            : ReferenceEnhancementBrowseSupport.NormalizeFilter(filter, bounds);
        if (normalizedFilter.MaxLevel > MaxReferenceLevel)
        {
            normalizedFilter = new ReferenceEnhancementBrowseFilter
            {
                MinLevel = Math.Min(normalizedFilter.MinLevel, MaxReferenceLevel),
                MaxLevel = MaxReferenceLevel,
                Rarity = normalizedFilter.Rarity
            };
        }

        var recipes = catalog.GetRecipes();
        var recipesBySet = new Dictionary<string, List<ItemReferenceRecord>>(StringComparer.Ordinal);
        var commonRecipes = new List<ItemReferenceRecord>();
        foreach (var recipe in recipes)
        {
            if (!MatchesRarityFilter(recipe, normalizedFilter.Rarity)
                || !MatchesLevelFilter(recipe, normalizedFilter))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(recipe.EnhancementSetId))
            {
                if (!recipesBySet.TryGetValue(recipe.EnhancementSetId, out var setRecipes))
                {
                    setRecipes = [];
                    recipesBySet[recipe.EnhancementSetId] = setRecipes;
                }

                setRecipes.Add(recipe);
                continue;
            }

            commonRecipes.Add(recipe);
        }

        var sets = catalog.GetEnhancementSets(ReferenceCatalogQueryScope.AllHomecomingIdentities);
        var categoryGroups = recipesBySet.Keys
            .Select(setId => sets.TryGetValue(setId, out var set) ? set : null)
            .Where(set => set is not null)
            .Cast<EnhancementSetReferenceRecord>()
            .GroupBy(set => ResolveCategoryKey(set), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => ResolveCategoryDisplayName(group.First()), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var categoryNodes = new List<ReferenceEnhancementBrowseNode>(categoryGroups.Length);
        foreach (var categoryGroup in categoryGroups)
        {
            var categoryDisplayName = ResolveCategoryDisplayName(categoryGroup.First());
            var categoryKey = categoryGroup.Key;
            var categoryNodeKey = $"recipe-category:{categoryKey}";
            var setNodes = categoryGroup
                .OrderBy(set => set.CurrentDisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(set => CreateSetNode(
                    set,
                    recipesBySet[set.CatalogItemId],
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
            DisplayName = "Recipe Sets",
            NodeKey = SetsRootNodeKey,
            Children = categoryNodes
        };

        var commonGroups = commonRecipes
            .GroupBy(recipe => ResolveCommonGroupKey(catalog, recipe), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var commonGroupNodes = new List<ReferenceEnhancementBrowseNode>(commonGroups.Length);
        foreach (var commonGroup in commonGroups)
        {
            var groupNodeKey = $"recipe-common-group:{commonGroup.Key}";
            var recipeNodes = commonGroup
                .OrderBy(recipe => recipe.CurrentDisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(recipe => CreateRecipeNode(recipe, groupNodeKey, parentSetId: null, normalizedFilter))
                .ToArray();
            if (recipeNodes.Length == 0)
            {
                continue;
            }

            commonGroupNodes.Add(new ReferenceEnhancementBrowseNode
            {
                Kind = ReferenceEnhancementBrowseNodeKind.CommonGroup,
                DisplayName = ResolveCommonGroupLabel(catalog, commonGroup.First()),
                NodeKey = groupNodeKey,
                ParentNodeKey = CommonRootNodeKey,
                CommonIoBoostType = commonGroup.Key,
                Children = recipeNodes
            });
        }

        var commonRoot = new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.RootCommonBranch,
            DisplayName = "Common Invention Recipes",
            NodeKey = CommonRootNodeKey,
            Children = commonGroupNodes
        };

        var rootNodes = new List<ReferenceEnhancementBrowseNode>(2);
        if (categoryNodes.Count > 0)
        {
            rootNodes.Add(setsRoot);
        }

        if (commonGroupNodes.Count > 0)
        {
            rootNodes.Add(commonRoot);
        }

        return new ReferenceEnhancementBrowseTree
        {
            RootNodes = rootNodes,
            NodesByKey = IndexNodes(rootNodes),
            Census = new ReferenceEnhancementBrowseCensus
            {
                CurrentSetCount = recipesBySet.Count,
                CommonInventionEnhancementCount = commonRecipes.Count,
                CategoryCount = categoryNodes.Count,
                CommonGroupCount = commonGroupNodes.Count
            }
        };
    }

    public static ReferenceEnhancementDetailModel BuildDetail(
        IItemReferenceCatalog catalog,
        ReferenceEnhancementBrowseNode? selectedNode,
        ReferenceEnhancementBrowseTree browseTree,
        IEnhancementIconCompositor? iconCompositor = null,
        IHomecomingBoostMetadataProvider? boostMetadata = null,
        IInstalledGameAssetProvider? assetProvider = null,
        int? presentationLevel = null,
        IEnhancementHelpResolver? helpResolver = null)
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
                Title = "Recipes",
                Summary = "Select a Recipe or Recipe Set to view details."
            };
        }

        return selectedNode.Kind switch
        {
            ReferenceEnhancementBrowseNodeKind.RootSetsBranch =>
                BuildBranchDetail(
                    "Recipe Sets",
                    "Browse invention Recipes grouped by Enhancement Set category.",
                    selectedNode),
            ReferenceEnhancementBrowseNodeKind.RootCommonBranch =>
                BuildBranchDetail(
                    "Common Invention Recipes",
                    "Browse common invention Recipes grouped by structural type.",
                    selectedNode),
            ReferenceEnhancementBrowseNodeKind.Category =>
                BuildCategoryDetail(selectedNode),
            ReferenceEnhancementBrowseNodeKind.Set =>
                BuildSetDetail(catalog, selectedNode, iconCompositor, boostMetadata),
            ReferenceEnhancementBrowseNodeKind.CommonGroup =>
                BuildCommonGroupDetail(catalog, selectedNode, iconCompositor, boostMetadata),
            ReferenceEnhancementBrowseNodeKind.Recipe =>
                BuildRecipeDetail(
                    catalog,
                    selectedNode.RecipeId,
                    presentationLevel,
                    iconCompositor,
                    boostMetadata,
                    assetProvider,
                    helpResolver),
            ReferenceEnhancementBrowseNodeKind.Level =>
                BuildRecipeDetail(
                    catalog,
                    selectedNode.RecipeId,
                    selectedNode.PresentationLevel,
                    iconCompositor,
                    boostMetadata,
                    assetProvider,
                    helpResolver),
            _ => new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Empty,
                Title = "Recipes",
                Summary = "Select a Recipe or Recipe Set to view details."
            }
        };
    }

    public static ImageSource? ResolveProducedEnhancementIcon(
        IItemReferenceCatalog catalog,
        string? producedItemId,
        IEnhancementIconCompositor? iconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadata = null)
    {
        if (!TryResolveProducedEnhancement(catalog, producedItemId, out var produced, out var parentSet))
        {
            return null;
        }

        return ReferenceEnhancementBrowseSupport.ResolveComposedIconSource(
            iconCompositor,
            catalog,
            produced,
            parentSet,
            boostMetadata);
    }

    public static EnhancementIconCompositionRequest? TryBuildProducedEnhancementCompositionRequest(
        IItemReferenceCatalog catalog,
        string? producedItemId,
        IHomecomingBoostMetadataProvider? boostMetadata = null)
    {
        if (!TryResolveProducedEnhancement(catalog, producedItemId, out var produced, out var parentSet))
        {
            return null;
        }

        return ReferenceEnhancementBrowseSupport.TryBuildCompositionRequest(
            catalog,
            produced,
            parentSet,
            boostMetadata);
    }

    public static ReferenceEnhancementBrowseNode? FindNodeForRecipeId(
        ReferenceEnhancementBrowseTree browseTree,
        string recipeId) =>
        browseTree.NodesByKey.TryGetValue(CreateRecipeNodeKey(recipeId), out var node) ? node : null;

    public static ReferenceEnhancementBrowseNode? FindNodeForRecipeLevel(
        ReferenceEnhancementBrowseTree browseTree,
        string recipeId,
        int level) =>
        browseTree.NodesByKey.TryGetValue(CreateRecipeLevelNodeKey(recipeId, level), out var node)
            ? node
            : null;

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

        if (!TryParseRecipeLevelNodeKey(previousNodeKey, out var recipeId, out var previousLevel))
        {
            return FindNodeForRecipeId(browseTree, TryParseRecipeId(previousNodeKey) ?? string.Empty)?.NodeKey;
        }

        var recipeNode = FindNodeForRecipeId(browseTree, recipeId);
        if (recipeNode is null)
        {
            return null;
        }

        var replacementLevel = recipeNode.Children
            .Where(child => child.Kind == ReferenceEnhancementBrowseNodeKind.Level && child.PresentationLevel.HasValue)
            .Select(child => child.PresentationLevel!.Value)
            .OrderBy(level => Math.Abs(level - previousLevel))
            .ThenBy(level => level)
            .Cast<int?>()
            .FirstOrDefault();

        return replacementLevel is not null
            ? CreateRecipeLevelNodeKey(recipeId, replacementLevel.Value)
            : recipeNode.NodeKey;
    }

    public static string CreateRecipeNodeKey(string recipeId) => $"recipe:{recipeId}";

    public static string CreateRecipeLevelNodeKey(string recipeId, int level) =>
        $"recipe:{recipeId}:lvl:{level}";

    internal static IReadOnlyList<int> GetVisibleLevels(
        ItemReferenceRecord recipe,
        ReferenceEnhancementBrowseFilter filter) =>
        recipe.ValidLevels
            .Where(level => level is >= 1 and <= MaxReferenceLevel
                && level >= filter.MinLevel
                && level <= Math.Min(filter.MaxLevel, MaxReferenceLevel))
            .Distinct()
            .OrderBy(level => level)
            .ToArray();

    private static ReferenceEnhancementBrowseNode CreateSetNode(
        EnhancementSetReferenceRecord set,
        IReadOnlyList<ItemReferenceRecord> setRecipes,
        string categoryNodeKey,
        ReferenceEnhancementBrowseFilter filter)
    {
        var setNodeKey = $"set:{set.CatalogItemId}";
        var recipeNodes = setRecipes
            .OrderBy(recipe => recipe.CurrentDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(recipe => CreateRecipeNode(recipe, setNodeKey, set.CatalogItemId, filter))
            .ToArray();
        var iconIdentity = recipeNodes
            .Select(node => node.IconIdentity)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.Set,
            DisplayName = set.CurrentDisplayName,
            NodeKey = setNodeKey,
            ParentNodeKey = categoryNodeKey,
            SetId = set.CatalogItemId,
            RecipeId = recipeNodes.FirstOrDefault()?.RecipeId,
            RarityCode = set.RarityCode,
            CategoryKey = ResolveCategoryKey(set),
            IconIdentity = iconIdentity,
            Children = recipeNodes
        };
    }

    private static ReferenceEnhancementBrowseNode CreateRecipeNode(
        ItemReferenceRecord recipe,
        string parentNodeKey,
        string? parentSetId,
        ReferenceEnhancementBrowseFilter filter)
    {
        var recipeNodeKey = CreateRecipeNodeKey(recipe.CatalogItemId);
        var levelNodes = GetVisibleLevels(recipe, filter)
            .Select(level => new ReferenceEnhancementBrowseNode
            {
                Kind = ReferenceEnhancementBrowseNodeKind.Level,
                DisplayName = $"Level {level}",
                NodeKey = CreateRecipeLevelNodeKey(recipe.CatalogItemId, level),
                ParentNodeKey = recipeNodeKey,
                RecipeId = recipe.CatalogItemId,
                EnhancementId = recipe.ProducedItemId,
                SetId = parentSetId,
                RarityCode = ToRarityCode(recipe.Rarity),
                IconIdentity = recipe.Icon,
                PresentationLevel = level
            })
            .ToArray();

        return new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.Recipe,
            DisplayName = recipe.CurrentDisplayName,
            NodeKey = recipeNodeKey,
            ParentNodeKey = parentNodeKey,
            RecipeId = recipe.CatalogItemId,
            EnhancementId = recipe.ProducedItemId,
            SetId = parentSetId,
            RarityCode = ToRarityCode(recipe.Rarity),
            IconIdentity = recipe.Icon,
            Children = levelNodes
        };
    }

    private static ReferenceEnhancementDetailModel BuildBranchDetail(
        string title,
        string summary,
        ReferenceEnhancementBrowseNode selectedNode) =>
        new()
        {
            Kind = ReferenceEnhancementDetailKind.Branch,
            Title = title,
            Summary = summary,
            IconPlaceholderLetter = CreatePlaceholderLetter(title),
            BranchItems = selectedNode.Children
                .Select(child => new ReferenceEnhancementBranchSummaryItem
                {
                    Label = child.DisplayName,
                    Count = child.Children.Count,
                    TargetNodeKey = child.NodeKey
                })
                .ToArray()
        };

    private static ReferenceEnhancementDetailModel BuildCategoryDetail(
        ReferenceEnhancementBrowseNode categoryNode) =>
        new()
        {
            Kind = ReferenceEnhancementDetailKind.Category,
            Title = categoryNode.DisplayName,
            Summary = $"{categoryNode.Children.Count} Recipe Sets",
            IconPlaceholderLetter = CreatePlaceholderLetter(categoryNode.DisplayName),
            SetItems = categoryNode.Children
                .Select(child => new ReferenceEnhancementSetSummaryItem
                {
                    SetId = child.SetId!,
                    DisplayName = child.DisplayName,
                    IconIdentity = child.IconIdentity,
                    IconPlaceholderLetter = CreatePlaceholderLetter(child.DisplayName)
                })
                .ToArray()
        };

    private static ReferenceEnhancementDetailModel BuildSetDetail(
        IItemReferenceCatalog catalog,
        ReferenceEnhancementBrowseNode setNode,
        IEnhancementIconCompositor? iconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadata)
    {
        var setName = setNode.DisplayName;
        EnhancementSetReferenceRecord? set = null;
        if (!string.IsNullOrWhiteSpace(setNode.SetId)
            && catalog.TryGetEnhancementSetById(setNode.SetId, out var resolvedSet))
        {
            set = resolvedSet;
            setName = resolvedSet.CurrentDisplayName;
        }

        return new ReferenceEnhancementDetailModel
        {
            Kind = ReferenceEnhancementDetailKind.Set,
            Title = setName,
            Summary = $"{setNode.Children.Count} Recipes",
            CategoryLabel = setNode.CategoryKey,
            RarityCode = setNode.RarityCode,
            IconIdentity = setNode.IconIdentity,
            IconSource = set is null
                ? null
                : ReferenceEnhancementBrowseSupport.ResolveSetComposedIconSource(
                    iconCompositor,
                    catalog,
                    set,
                    boostMetadata),
            IconPlaceholderLetter = CreatePlaceholderLetter(setName),
            MemberItems = setNode.Children
                .Select(child => new ReferenceEnhancementMemberSummaryItem
                {
                    EnhancementId = child.EnhancementId ?? string.Empty,
                    TargetNodeKey = child.NodeKey,
                    DisplayName = child.DisplayName,
                    IconIdentity = child.IconIdentity,
                    IconSource = ResolveProducedEnhancementIcon(
                        catalog,
                        child.EnhancementId,
                        iconCompositor,
                        boostMetadata),
                    IconPlaceholderLetter = CreatePlaceholderLetter(child.DisplayName)
                })
                .ToArray()
        };
    }

    private static ReferenceEnhancementDetailModel BuildCommonGroupDetail(
        IItemReferenceCatalog catalog,
        ReferenceEnhancementBrowseNode groupNode,
        IEnhancementIconCompositor? iconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadata) =>
        new()
        {
            Kind = ReferenceEnhancementDetailKind.Category,
            Title = groupNode.DisplayName,
            Summary = $"{groupNode.Children.Count} Recipes",
            IconPlaceholderLetter = CreatePlaceholderLetter(groupNode.DisplayName),
            MemberItems = groupNode.Children
                .Select(child => new ReferenceEnhancementMemberSummaryItem
                {
                    EnhancementId = child.EnhancementId ?? string.Empty,
                    TargetNodeKey = child.NodeKey,
                    DisplayName = child.DisplayName,
                    IconIdentity = child.IconIdentity,
                    IconSource = ResolveProducedEnhancementIcon(
                        catalog,
                        child.EnhancementId,
                        iconCompositor,
                        boostMetadata),
                    IconPlaceholderLetter = CreatePlaceholderLetter(child.DisplayName)
                })
                .ToArray()
        };

    private static ReferenceEnhancementDetailModel BuildRecipeDetail(
        IItemReferenceCatalog catalog,
        string? recipeId,
        int? presentationLevel,
        IEnhancementIconCompositor? iconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadata,
        IInstalledGameAssetProvider? assetProvider,
        IEnhancementHelpResolver? helpResolver)
    {
        if (string.IsNullOrWhiteSpace(recipeId) || !catalog.TryGetById(recipeId, out var recipe)
            || recipe.Family != ReferenceItemFamily.Recipe)
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Empty,
                Title = "Recipes",
                Summary = "Select a Recipe or Recipe Set to view details."
            };
        }

        var selectedLevel = ResolveSelectedLevel(recipe, presentationLevel);
        var levelRecord = recipe.RecipeLevels.FirstOrDefault(level => level.Level == selectedLevel);
        ItemReferenceRecord? produced = null;
        if (!string.IsNullOrWhiteSpace(recipe.ProducedItemId)
            && catalog.TryGetById(recipe.ProducedItemId, out var producedItem)
            && producedItem.Family == ReferenceItemFamily.Enhancement)
        {
            produced = producedItem;
        }

        string? parentSetName = null;
        if (!string.IsNullOrWhiteSpace(recipe.EnhancementSetId)
            && catalog.TryGetEnhancementSetById(recipe.EnhancementSetId, out var parentSet))
        {
            parentSetName = parentSet.CurrentDisplayName;
        }

        var validLevels = recipe.ValidLevels
            .Where(level => level is >= 1 and <= MaxReferenceLevel)
            .ToArray();
        var producedIconIdentity = produced?.Icon ?? produced?.SourceVariants.FirstOrDefault()?.Icon;
        string? resolvedDisplayHelp = null;
        string? canonicalDisplayHelp = null;
        EnhancementHelpResolutionStatus? helpResolutionStatus = null;
        var showHelpResolutionNote = false;
        if (produced is not null && selectedLevel is int helpLevel)
        {
            (resolvedDisplayHelp, canonicalDisplayHelp, helpResolutionStatus, showHelpResolutionNote) =
                ReferenceEnhancementBrowseSupport.ResolveEnhancementDisplayHelp(
                    produced,
                    helpResolver,
                    helpLevel);
        }

        return new ReferenceEnhancementDetailModel
        {
            Kind = ReferenceEnhancementDetailKind.Recipe,
            Title = recipe.CurrentDisplayName,
            CategoryLabel = parentSetName,
            RarityLabel = recipe.Rarity,
            RarityCode = ToRarityCode(recipe.Rarity),
            LevelRangeLabel = FormatLevelRange(validLevels),
            ParentSetName = parentSetName,
            ParentSetId = recipe.EnhancementSetId,
            IconIdentity = producedIconIdentity,
            IconSource = ResolveProducedEnhancementIcon(
                catalog,
                recipe.ProducedItemId,
                iconCompositor,
                boostMetadata),
            IconPlaceholderLetter = CreatePlaceholderLetter(recipe.CurrentDisplayName),
            ProducedEnhancementId = recipe.ProducedItemId,
            ProducedEnhancementName = produced?.CurrentDisplayName,
            LogicalHelp = resolvedDisplayHelp,
            CanonicalDisplayHelp = canonicalDisplayHelp,
            ResolvedDisplayHelp = resolvedDisplayHelp,
            HelpResolutionStatus = helpResolutionStatus,
            ShowHelpResolutionNote = showHelpResolutionNote,
            PresentationLevel = selectedLevel ?? 0,
            PresentationLevelLabel = selectedLevel is null
                ? null
                : $"Crafting values for Level {selectedLevel.Value}",
            CraftingCostLabel = levelRecord is null
                ? null
                : levelRecord.CraftingCost.ToString("N0", CultureInfo.InvariantCulture),
            SalvageRequirements = levelRecord is null
                ? []
                : OrderSalvageRequirements(catalog, levelRecord.Requirements)
                    .Select(requirement => CreateSalvageRequirement(catalog, requirement, assetProvider))
                    .ToArray()
        };
    }

    private static IEnumerable<RecipeRequirementReferenceRecord> OrderSalvageRequirements(
        IItemReferenceCatalog catalog,
        IReadOnlyList<RecipeRequirementReferenceRecord> requirements) =>
        requirements
            .Select((requirement, index) => (requirement, index))
            .OrderBy(item => SalvageRaritySortKey(ResolveSalvageRarity(catalog, item.requirement.SalvageItemId)))
            .ThenBy(item => item.index)
            .Select(item => item.requirement);

    private static string? ResolveSalvageRarity(IItemReferenceCatalog catalog, string salvageItemId) =>
        catalog.TryGetById(salvageItemId, out var salvage) && salvage.Family == ReferenceItemFamily.Salvage
            ? salvage.Rarity
            : null;

    private static int SalvageRaritySortKey(string? rarity) =>
        rarity switch
        {
            "Common" => 0,
            "Uncommon" => 1,
            "Rare" => 2,
            "Very Rare" => 3,
            _ => 4
        };

    private static ReferenceRecipeSalvageRequirementItem CreateSalvageRequirement(
        IItemReferenceCatalog catalog,
        RecipeRequirementReferenceRecord requirement,
        IInstalledGameAssetProvider? assetProvider)
    {
        var displayName = requirement.SalvageItemId;
        string? iconIdentity = null;
        string? rarity = null;
        if (catalog.TryGetById(requirement.SalvageItemId, out var salvage)
            && salvage.Family == ReferenceItemFamily.Salvage)
        {
            displayName = salvage.CurrentDisplayName;
            iconIdentity = salvage.Icon;
            rarity = salvage.Rarity;
        }

        return new ReferenceRecipeSalvageRequirementItem
        {
            SalvageItemId = requirement.SalvageItemId,
            DisplayName = displayName,
            Quantity = requirement.Quantity,
            IconPlaceholderLetter = CreatePlaceholderLetter(displayName),
            IconSource = assetProvider?.TryResolve(iconIdentity),
            RarityCode = ToRarityCode(rarity)
        };
    }

    private static int? ResolveSelectedLevel(ItemReferenceRecord recipe, int? presentationLevel)
    {
        var validLevels = recipe.ValidLevels
            .Where(level => level is >= 1 and <= MaxReferenceLevel)
            .ToArray();
        if (validLevels.Length == 0)
        {
            return null;
        }

        if (presentationLevel is int requested
            && requested is >= 1 and <= MaxReferenceLevel
            && validLevels.Contains(requested))
        {
            return requested;
        }

        return validLevels.Max();
    }

    private static bool MatchesRarityFilter(ItemReferenceRecord recipe, string? rarityFilter) =>
        rarityFilter is null
        || string.Equals(recipe.Rarity, rarityFilter, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesLevelFilter(
        ItemReferenceRecord recipe,
        ReferenceEnhancementBrowseFilter filter) =>
        GetVisibleLevels(recipe, filter).Count > 0;

    private static string ResolveCategoryKey(EnhancementSetReferenceRecord set) =>
        set.CategoryDisplayText
        ?? set.CategoryCode
        ?? "Uncategorized";

    private static string ResolveCategoryDisplayName(EnhancementSetReferenceRecord set) =>
        set.CategoryDisplayText
        ?? set.CategoryCode
        ?? "Uncategorized";

    private static string ResolveCommonGroupKey(IItemReferenceCatalog catalog, ItemReferenceRecord recipe)
    {
        if (!string.IsNullOrWhiteSpace(recipe.ProducedItemId)
            && catalog.TryGetById(recipe.ProducedItemId, out var produced)
            && !string.IsNullOrWhiteSpace(produced.CommonIoBoostType))
        {
            return produced.CommonIoBoostType;
        }

        return recipe.CatalogItemId;
    }

    private static string ResolveCommonGroupLabel(IItemReferenceCatalog catalog, ItemReferenceRecord recipe)
    {
        if (!string.IsNullOrWhiteSpace(recipe.ProducedItemId)
            && catalog.TryGetById(recipe.ProducedItemId, out var produced))
        {
            return produced.CommonIoBoostTypeDisplayText
                ?? produced.CommonIoBoostType
                ?? recipe.CurrentDisplayName;
        }

        return recipe.CurrentDisplayName;
    }

    private static string? ToRarityCode(string? rarity) =>
        rarity switch
        {
            "Uncommon" => "ECUncommon",
            "Rare" => "ECRare",
            "Very Rare" => "ECVeryRare",
            _ => null
        };

    private static string RaritySortKey(string? rarity) =>
        rarity switch
        {
            "Common" => "1",
            "Uncommon" => "2",
            "Rare" => "3",
            "Very Rare" => "4",
            _ => "9"
        };

    private static string? FormatLevelRange(IReadOnlyList<int> levels)
    {
        if (levels.Count == 0)
        {
            return null;
        }

        return levels[0] == levels[^1]
            ? $"Level {levels[0]}"
            : $"Levels {levels[0]}–{levels[^1]}";
    }

    private static string CreatePlaceholderLetter(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "?" : char.ToUpperInvariant(text[0]).ToString();

    private static bool TryParseRecipeLevelNodeKey(
        string nodeKey,
        out string recipeId,
        out int presentationLevel)
    {
        recipeId = string.Empty;
        presentationLevel = 0;
        const string prefix = "recipe:";
        const string levelMarker = ":lvl:";
        if (!nodeKey.StartsWith(prefix, StringComparison.Ordinal)
            || !nodeKey.Contains(levelMarker, StringComparison.Ordinal))
        {
            return false;
        }

        var levelIndex = nodeKey.LastIndexOf(levelMarker, StringComparison.Ordinal);
        recipeId = nodeKey[prefix.Length..levelIndex];
        return int.TryParse(
            nodeKey.AsSpan(levelIndex + levelMarker.Length),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out presentationLevel);
    }

    private static string? TryParseRecipeId(string nodeKey)
    {
        const string prefix = "recipe:";
        if (!nodeKey.StartsWith(prefix, StringComparison.Ordinal)
            || nodeKey.Contains(":lvl:", StringComparison.Ordinal))
        {
            return null;
        }

        var recipeId = nodeKey[prefix.Length..];
        return string.IsNullOrWhiteSpace(recipeId) ? null : recipeId;
    }

    private static bool TryResolveProducedEnhancement(
        IItemReferenceCatalog catalog,
        string? producedItemId,
        out ItemReferenceRecord produced,
        out EnhancementSetReferenceRecord? parentSet)
    {
        produced = null!;
        parentSet = null;
        if (string.IsNullOrWhiteSpace(producedItemId)
            || !catalog.TryGetById(producedItemId, out var item)
            || item.Family != ReferenceItemFamily.Enhancement)
        {
            return false;
        }

        produced = item;
        if (!string.IsNullOrWhiteSpace(item.EnhancementSetId)
            && catalog.TryGetEnhancementSetById(item.EnhancementSetId, out var set))
        {
            parentSet = set;
        }

        return true;
    }

    private static Dictionary<string, ReferenceEnhancementBrowseNode> IndexNodes(
        IReadOnlyList<ReferenceEnhancementBrowseNode> roots)
    {
        var index = new Dictionary<string, ReferenceEnhancementBrowseNode>(StringComparer.Ordinal);
        void Walk(ReferenceEnhancementBrowseNode node)
        {
            index[node.NodeKey] = node;
            foreach (var child in node.Children)
            {
                Walk(child);
            }
        }

        foreach (var root in roots)
        {
            Walk(root);
        }

        return index;
    }
}

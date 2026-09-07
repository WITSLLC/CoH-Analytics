using System.Collections.ObjectModel;
using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.ReferenceData;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoHAnalytics.ViewModels.Workspaces;

public sealed partial class ReferenceEnhancementTreeNodeViewModel : ObservableObject
{
    public ReferenceEnhancementTreeNodeViewModel(
        ReferenceEnhancementBrowseNode node,
        IItemReferenceCatalog catalog,
        IEnhancementIconCompositor? iconCompositor = null,
        IHomecomingBoostMetadataProvider? boostMetadata = null,
        IInstalledGameAssetProvider? installedGameAssetProvider = null)
    {
        Kind = node.Kind;
        NodeKey = node.NodeKey;
        ParentNodeKey = node.ParentNodeKey;
        DisplayName = node.DisplayName;
        SetId = node.SetId;
        RarityCode = node.RarityCode;
        EnhancementId = node.EnhancementId;
        RecipeId = node.RecipeId;
        BadgeId = node.BadgeId;
        SecondaryLine = node.SecondaryLine;
        IsAcquired = node.IsAcquired;
        IconIdentity = node.IconIdentity;
        IconSource = ResolveTreeIconSource(
            catalog,
            node,
            iconCompositor,
            boostMetadata,
            installedGameAssetProvider);
        IconPlaceholderLetter = string.IsNullOrWhiteSpace(node.DisplayName)
            ? "?"
            : char.ToUpperInvariant(node.DisplayName[0]).ToString();
        PresentationLevel = node.PresentationLevel;

        foreach (var child in node.Children)
        {
            Children.Add(new ReferenceEnhancementTreeNodeViewModel(
                child,
                catalog,
                iconCompositor,
                boostMetadata,
                installedGameAssetProvider));
        }
    }

    public ReferenceEnhancementBrowseNodeKind Kind { get; }

    public string NodeKey { get; }

    public string? ParentNodeKey { get; }

    public string DisplayName { get; }

    public string? SetId { get; }

    public string? RarityCode { get; }

    public string? EnhancementId { get; }

    public string? RecipeId { get; }

    public string? BadgeId { get; }

    public string? SecondaryLine { get; }

    [ObservableProperty]
    private bool _isAcquired;

    public string? IconIdentity { get; }

    public ImageSource? IconSource { get; }

    public string IconPlaceholderLetter { get; }

    public int? PresentationLevel { get; }

    public ObservableCollection<ReferenceEnhancementTreeNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isSearchHighlighted;

    internal void ApplyAcquiredState(bool isAcquired)
    {
        if (IsAcquired != isAcquired)
        {
            IsAcquired = isAcquired;
        }
    }

    private static ImageSource? ResolveTreeIconSource(
        IItemReferenceCatalog catalog,
        ReferenceEnhancementBrowseNode node,
        IEnhancementIconCompositor? iconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadata,
        IInstalledGameAssetProvider? installedGameAssetProvider)
    {
        if ((node.Kind == ReferenceEnhancementBrowseNodeKind.Badge
                || node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccolade)
            && !string.IsNullOrWhiteSpace(node.IconIdentity))
        {
            return installedGameAssetProvider?.TryResolve(node.IconIdentity);
        }

        if (node.Kind == ReferenceEnhancementBrowseNodeKind.Recipe
            || (node.Kind == ReferenceEnhancementBrowseNodeKind.Level
                && !string.IsNullOrWhiteSpace(node.RecipeId)))
        {
            return ReferenceRecipeBrowseSupport.ResolveProducedEnhancementIcon(
                catalog,
                node.EnhancementId,
                iconCompositor,
                boostMetadata);
        }

        if (node.Kind == ReferenceEnhancementBrowseNodeKind.Set
            && !string.IsNullOrWhiteSpace(node.SetId)
            && catalog.TryGetEnhancementSetById(node.SetId, out var set))
        {
            return ReferenceEnhancementBrowseSupport.ResolveSetComposedIconSource(
                iconCompositor,
                catalog,
                set,
                boostMetadata);
        }

        if (node.Kind != ReferenceEnhancementBrowseNodeKind.Enhancement
            || string.IsNullOrWhiteSpace(node.EnhancementId)
            || !catalog.TryGetById(node.EnhancementId, out var item))
        {
            return null;
        }

        EnhancementSetReferenceRecord? parentSet = null;
        if (!string.IsNullOrWhiteSpace(item.EnhancementSetId)
            && catalog.TryGetEnhancementSetById(item.EnhancementSetId, out var resolvedSet))
        {
            parentSet = resolvedSet;
        }

        return ReferenceEnhancementBrowseSupport.ResolveComposedIconSource(
            iconCompositor,
            catalog,
            item,
            parentSet,
            boostMetadata);
    }
}

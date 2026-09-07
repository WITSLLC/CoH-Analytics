using System.Collections.ObjectModel;
using System.Windows.Media;
using CoHAnalytics.ReferenceData;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoHAnalytics.ViewModels.Workspaces;

public sealed partial class ReferenceEnhancementDetailViewModel : ObservableObject
{
    public void Apply(ReferenceEnhancementDetailModel model)
    {
        Kind = model.Kind;
        ErrorMessage = model.ErrorMessage;
        Title = model.Title ?? string.Empty;
        Summary = model.Summary ?? string.Empty;
        CategoryLabel = model.CategoryLabel;
        RarityLabel = model.RarityLabel;
        RarityCode = model.RarityCode;
        LevelRangeLabel = model.LevelRangeLabel;
        ParentSetName = model.ParentSetName;
        ParentSetId = model.ParentSetId;
        FamilyLabel = model.FamilyLabel;
        IconIdentity = model.IconIdentity;
        IconSource = model.IconSource;
        IconPlaceholderLetter = model.IconPlaceholderLetter;
        LogicalHelp = model.LogicalHelp;
        CanonicalDisplayHelp = model.CanonicalDisplayHelp;
        ResolvedDisplayHelp = model.ResolvedDisplayHelp;
        HelpResolutionStatus = model.HelpResolutionStatus;
        PresentationLevel = model.PresentationLevel;
        PresentationLevelLabel = model.PresentationLevelLabel;
        ShowHelpResolutionNote = model.ShowHelpResolutionNote;
        HelpResolutionNote = model.HelpResolutionNote;
        ShortHelp = model.ShortHelp;
        AvailableSourceFormsLabel = model.AvailableSourceFormsLabel;
        ShowVariantHelpSections = model.ShowVariantHelpSections;
        ShowRarity = !string.IsNullOrWhiteSpace(model.RarityLabel);
        ShowParentSet = !string.IsNullOrWhiteSpace(model.ParentSetId);
        ShowSetBonuses = model.SetBonusTiers.Count > 0;
        ShowMembers = model.MemberItems.Count > 0;
        ShowSets = model.SetItems.Count > 0;
        ShowBranchItems = model.BranchItems.Count > 0;
        ProducedEnhancementId = model.ProducedEnhancementId;
        ProducedEnhancementName = model.ProducedEnhancementName;
        ShowProducedEnhancement = !string.IsNullOrWhiteSpace(model.ProducedEnhancementId)
            && !string.IsNullOrWhiteSpace(model.ProducedEnhancementName);
        CraftingCostLabel = model.CraftingCostLabel;
        ShowCraftingCost = !string.IsNullOrWhiteSpace(model.CraftingCostLabel);
        ShowPresentationLevel =
            (model.Kind is ReferenceEnhancementDetailKind.Enhancement or ReferenceEnhancementDetailKind.Recipe)
            && !string.IsNullOrWhiteSpace(model.PresentationLevelLabel);

        BranchItems.Clear();
        foreach (var item in model.BranchItems)
        {
            BranchItems.Add(item);
        }

        SetItems.Clear();
        foreach (var item in model.SetItems)
        {
            SetItems.Add(item);
        }

        MemberItems.Clear();
        foreach (var item in model.MemberItems)
        {
            MemberItems.Add(item);
        }

        SetBonusTiers.Clear();
        foreach (var tier in model.SetBonusTiers)
        {
            SetBonusTiers.Add(tier);
        }

        VariantHelpSections.Clear();
        foreach (var section in model.VariantHelpSections)
        {
            VariantHelpSections.Add(section);
        }

        SalvageRequirements.Clear();
        foreach (var requirement in model.SalvageRequirements)
        {
            SalvageRequirements.Add(requirement);
        }

        ShowSalvageRequirements = SalvageRequirements.Count > 0;
        AcquiredStateLabel = model.AcquiredStateLabel;
        ShowAcquiredState = model.ShowAcquiredState;
        ZoneLabel = model.ZoneLabel;
        ShowZone = model.ShowZone;
        ThumbtackCommand = model.ThumbtackCommand;
        ShowThumbtack = model.ShowThumbtack;
        LocationNote = model.LocationNote;
        ShowLocationNote = model.ShowLocationNote;
        RewardText = model.RewardText;
        ShowReward = model.ShowReward;
        UseWideBadgeIcon = model.UseWideBadgeIcon;
        RequirementIntroText = model.RequirementIntroText;
        ShowRequirementIntro = model.ShowRequirementIntro;
        RequirementLogicNote = model.RequirementLogicNote;
        ShowRequirementLogicNote = model.ShowRequirementLogicNote;
        ShowAccoladeRequirements = model.ShowAccoladeRequirements;

        AccoladeRequirements.Clear();
        foreach (var requirement in model.AccoladeRequirements)
        {
            AccoladeRequirements.Add(requirement);
        }
    }

    [ObservableProperty]
    private ReferenceEnhancementDetailKind _kind;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string? _categoryLabel;

    [ObservableProperty]
    private string? _rarityLabel;

    [ObservableProperty]
    private string? _rarityCode;

    [ObservableProperty]
    private string? _levelRangeLabel;

    [ObservableProperty]
    private string? _parentSetName;

    [ObservableProperty]
    private string? _parentSetId;

    [ObservableProperty]
    private string? _familyLabel;

    [ObservableProperty]
    private string? _iconIdentity;

    [ObservableProperty]
    private ImageSource? _iconSource;

    [ObservableProperty]
    private string _iconPlaceholderLetter = "?";

    [ObservableProperty]
    private string? _logicalHelp;

    [ObservableProperty]
    private string? _canonicalDisplayHelp;

    [ObservableProperty]
    private string? _resolvedDisplayHelp;

    [ObservableProperty]
    private EnhancementHelpResolutionStatus? _helpResolutionStatus;

    [ObservableProperty]
    private int _presentationLevel;

    [ObservableProperty]
    private string? _presentationLevelLabel;

    [ObservableProperty]
    private bool _showHelpResolutionNote;

    [ObservableProperty]
    private string _helpResolutionNote = string.Empty;

    [ObservableProperty]
    private bool _showPresentationLevel;

    [ObservableProperty]
    private string? _shortHelp;

    [ObservableProperty]
    private string? _availableSourceFormsLabel;

    [ObservableProperty]
    private bool _showVariantHelpSections;

    [ObservableProperty]
    private bool _showRarity;

    [ObservableProperty]
    private bool _showParentSet;

    [ObservableProperty]
    private bool _showSetBonuses;

    [ObservableProperty]
    private bool _showMembers;

    [ObservableProperty]
    private bool _showSets;

    [ObservableProperty]
    private bool _showBranchItems;

    [ObservableProperty]
    private string? _producedEnhancementId;

    [ObservableProperty]
    private string? _producedEnhancementName;

    [ObservableProperty]
    private bool _showProducedEnhancement;

    [ObservableProperty]
    private string? _craftingCostLabel;

    [ObservableProperty]
    private bool _showCraftingCost;

    [ObservableProperty]
    private bool _showSalvageRequirements;

    [ObservableProperty]
    private string? _acquiredStateLabel;

    [ObservableProperty]
    private bool _showAcquiredState;

    [ObservableProperty]
    private string? _zoneLabel;

    [ObservableProperty]
    private bool _showZone;

    [ObservableProperty]
    private string? _thumbtackCommand;

    [ObservableProperty]
    private bool _showThumbtack;

    [ObservableProperty]
    private string? _locationNote;

    [ObservableProperty]
    private bool _showLocationNote;

    [ObservableProperty]
    private string? _rewardText;

    [ObservableProperty]
    private bool _showReward;

    [ObservableProperty]
    private bool _useWideBadgeIcon;

    [ObservableProperty]
    private string? _requirementIntroText;

    [ObservableProperty]
    private bool _showRequirementIntro;

    [ObservableProperty]
    private string? _requirementLogicNote;

    [ObservableProperty]
    private bool _showRequirementLogicNote;

    [ObservableProperty]
    private bool _showAccoladeRequirements;

    public ObservableCollection<ReferenceEnhancementBranchSummaryItem> BranchItems { get; } = [];

    public ObservableCollection<ReferenceEnhancementSetSummaryItem> SetItems { get; } = [];

    public ObservableCollection<ReferenceEnhancementMemberSummaryItem> MemberItems { get; } = [];

    public ObservableCollection<ReferenceEnhancementSetBonusTierDisplay> SetBonusTiers { get; } = [];

    public ObservableCollection<ReferenceEnhancementVariantHelpDisplay> VariantHelpSections { get; } = [];

    public ObservableCollection<ReferenceRecipeSalvageRequirementItem> SalvageRequirements { get; } = [];

    public ObservableCollection<ReferenceAccoladeRequirementItemDisplay> AccoladeRequirements { get; } = [];
}

public sealed partial class ReferenceSearchResultViewModel : ObservableObject
{
    public ReferenceSearchResultViewModel(string label, string targetNodeKey, string groupLabel)
    {
        Label = label;
        TargetNodeKey = targetNodeKey;
        GroupLabel = groupLabel;
    }

    public string Label { get; }

    public string TargetNodeKey { get; }

    public string GroupLabel { get; }
}

public enum ReferenceSectionId
{
    Enhancements = 0,
    Recipes = 1,
    Salvage = 2,
    Inspirations = 3,
    Badges = 4,
    Powers = 5
}

public sealed partial class ReferenceSectionChipViewModel : ObservableObject
{
    public ReferenceSectionChipViewModel(string label, ReferenceSectionId sectionId, bool isEnabled, bool isActive = false)
    {
        Label = label;
        SectionId = sectionId;
        IsEnabled = isEnabled;
        IsActive = isActive;
    }

    public string Label { get; }

    public ReferenceSectionId SectionId { get; }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isActive;
}

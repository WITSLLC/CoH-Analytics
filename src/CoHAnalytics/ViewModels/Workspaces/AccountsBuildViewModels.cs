using System.Windows.Media;

namespace CoHAnalytics.ViewModels.Workspaces;

public sealed record AccountsBuildPresentation(
    AccountsBuildPowerSetSectionViewModel? PrimarySection,
    AccountsBuildPowerSetSectionViewModel? SecondarySection,
    IReadOnlyList<AccountsBuildPowerSetSectionViewModel> AdditionalSections,
    int PowerCount);

public sealed record AccountsBuildPowerSetSectionViewModel(
    string DisplayName,
    IReadOnlyList<AccountsBuildPowerViewModel> Powers);

public sealed record AccountsBuildPowerViewModel(
    string DisplayName,
    string LevelLabel,
    ImageSource? IconSource,
    string Tooltip,
    IReadOnlyList<AccountsBuildEnhancementSlotViewModel> EnhancementSlots);

public sealed record AccountsBuildEnhancementSlotViewModel(
    ImageSource? IconSource,
    string Tooltip,
    bool IsEmpty);

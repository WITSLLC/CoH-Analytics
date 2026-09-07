using System.Collections.ObjectModel;
using System.Windows.Media;

namespace CoHAnalytics.ViewModels.Workspaces;

public sealed class CharacterBadgeCategoryGroupViewModel
{
    public CharacterBadgeCategoryGroupViewModel(
        string categoryName,
        IReadOnlyList<CharacterBadgeIconViewModel> badges)
    {
        CategoryName = categoryName;
        foreach (var badge in badges)
        {
            Badges.Add(badge);
        }
    }

    public string CategoryName { get; }

    public ObservableCollection<CharacterBadgeIconViewModel> Badges { get; } = [];
}

public sealed class CharacterBadgeIconViewModel
{
    internal CharacterBadgeIconViewModel(CharacterBadgeGalleryBadgeItem item)
    {
        DisplayName = item.DisplayName;
        IconSource = item.IconSource;
        UseWideBadgeIcon = item.UseWideBadgeIcon;
        IconPlaceholderLetter = item.IconPlaceholderLetter;
    }

    public string DisplayName { get; }

    public ImageSource? IconSource { get; }

    public bool UseWideBadgeIcon { get; }

    public string IconPlaceholderLetter { get; }

    public double IconWidth => UseWideBadgeIcon ? 72 : 48;
}

using System.Collections.ObjectModel;
using System.Windows.Media;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoHAnalytics.ViewModels.CharacterIcons;

public sealed partial class CharacterIconPickerViewModel : ObservableObject
{
    private readonly Func<string, IReadOnlyList<string>> _assignmentLookup;

    public CharacterIconPickerViewModel(
        IReadOnlyList<BuiltInCharacterIcon> icons,
        string? currentIconId)
        : this(
            icons,
            [],
            string.IsNullOrWhiteSpace(currentIconId)
                ? null
                : CharacterIconReference.BuiltIn(currentIconId),
            NullCustomCharacterIconService.Instance)
    {
    }

    public CharacterIconPickerViewModel(
        IReadOnlyList<BuiltInCharacterIcon> builtInIcons,
        IReadOnlyList<CustomCharacterIcon> customIcons,
        CharacterIconReference? currentIconReference,
        ICustomCharacterIconService customIconService,
        Func<string, IReadOnlyList<string>>? assignmentLookup = null)
    {
        ArgumentNullException.ThrowIfNull(builtInIcons);
        ArgumentNullException.ThrowIfNull(customIcons);
        CustomIconService = customIconService
            ?? throw new ArgumentNullException(nameof(customIconService));
        _assignmentLookup = assignmentLookup ?? (_ => []);

        BuiltInIcons = new ObservableCollection<CharacterIconPickerItemViewModel>(
            builtInIcons.Select((icon, index) => new CharacterIconPickerItemViewModel
            {
                Reference = CharacterIconReference.BuiltIn(icon.Id),
                ImageSource = icon.ImageSource,
                AccessibleName = $"Built-in character icon {index + 1}"
            }));
        CustomIcons = new ObservableCollection<CharacterIconPickerItemViewModel>(
            customIcons.Select((icon, index) => CreateCustomItem(icon, index)));
        SelectedIcon = BuiltInIcons.Concat(CustomIcons).FirstOrDefault(icon =>
            Equals(icon.Reference, currentIconReference));
    }

    public ObservableCollection<CharacterIconPickerItemViewModel> BuiltInIcons { get; }

    // Compatibility name for the established built-in picker contract and its tests.
    public ObservableCollection<CharacterIconPickerItemViewModel> Icons => BuiltInIcons;

    public ObservableCollection<CharacterIconPickerItemViewModel> CustomIcons { get; }

    internal ICustomCharacterIconService CustomIconService { get; }

    public bool HasCustomIcons => CustomIcons.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    private CharacterIconPickerItemViewModel? _selectedIcon;

    [ObservableProperty]
    private CharacterIconPickerItemViewModel? _selectedBuiltInIcon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelectedCustomIcon))]
    private CharacterIconPickerItemViewModel? _selectedCustomIcon;

    public bool CanApply => SelectedIcon is not null;

    public bool CanDeleteSelectedCustomIcon => SelectedCustomIcon is not null;

    partial void OnSelectedIconChanged(CharacterIconPickerItemViewModel? value)
    {
        if (value?.Reference.Kind == CharacterIconKind.BuiltIn)
        {
            SetProperty(ref _selectedBuiltInIcon, value, nameof(SelectedBuiltInIcon));
            SetProperty(ref _selectedCustomIcon, null, nameof(SelectedCustomIcon));
        }
        else if (value?.Reference.Kind == CharacterIconKind.Custom)
        {
            SetProperty(ref _selectedBuiltInIcon, null, nameof(SelectedBuiltInIcon));
            SetProperty(ref _selectedCustomIcon, value, nameof(SelectedCustomIcon));
        }
        else
        {
            SetProperty(ref _selectedBuiltInIcon, null, nameof(SelectedBuiltInIcon));
            SetProperty(ref _selectedCustomIcon, null, nameof(SelectedCustomIcon));
        }
    }

    partial void OnSelectedBuiltInIconChanged(CharacterIconPickerItemViewModel? value)
    {
        if (value is not null)
        {
            SelectedIcon = value;
        }
    }

    partial void OnSelectedCustomIconChanged(CharacterIconPickerItemViewModel? value)
    {
        if (value is not null)
        {
            SelectedIcon = value;
        }
    }

    public void AddAndSelectCustomIcon(CustomCharacterIcon icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        var existing = CustomIcons.FirstOrDefault(item =>
            string.Equals(item.IconId, icon.Id, StringComparison.Ordinal));
        if (existing is null)
        {
            existing = CreateCustomItem(icon, CustomIcons.Count);
            CustomIcons.Add(existing);
            OnPropertyChanged(nameof(HasCustomIcons));
        }

        SelectedIcon = existing;
    }

    public bool TrySaveAndSelectCustomIcon(byte[] normalizedPng, out string? errorMessage)
    {
        var result = CustomIconService.SaveNormalizedIcon(normalizedPng);
        if (!result.IsSuccess || result.Icon is null)
        {
            errorMessage = result.ErrorMessage ?? "The custom icon could not be saved.";
            return false;
        }

        AddAndSelectCustomIcon(result.Icon);
        errorMessage = null;
        return true;
    }

    public IReadOnlyList<string> GetSelectedCustomIconAssignments() =>
        SelectedCustomIcon is null
            ? []
            : _assignmentLookup(SelectedCustomIcon.IconId);

    public bool TryDeleteSelectedCustomIcon(out string? errorMessage)
    {
        var selected = SelectedCustomIcon;
        if (selected is null)
        {
            errorMessage = "Select a custom icon to delete.";
            return false;
        }

        if (_assignmentLookup(selected.IconId).Count > 0)
        {
            errorMessage = "The custom icon is still assigned to one or more characters.";
            return false;
        }

        var result = CustomIconService.DeleteIcon(selected.IconId);
        if (!result.IsSuccess)
        {
            errorMessage = result.ErrorMessage ?? "The custom icon could not be deleted.";
            return false;
        }

        SelectedIcon = null;
        CustomIcons.Remove(selected);
        OnPropertyChanged(nameof(HasCustomIcons));
        errorMessage = null;
        return true;
    }

    private static CharacterIconPickerItemViewModel CreateCustomItem(
        CustomCharacterIcon icon,
        int index) =>
        new()
        {
            Reference = CharacterIconReference.Custom(icon.Id),
            ImageSource = icon.ImageSource,
            AccessibleName = $"Custom character icon {index + 1}"
        };
}

public sealed class CharacterIconPickerItemViewModel
{
    public required CharacterIconReference Reference { get; init; }

    public string IconId => Reference.IconId;

    public required ImageSource ImageSource { get; init; }

    public required string AccessibleName { get; init; }
}

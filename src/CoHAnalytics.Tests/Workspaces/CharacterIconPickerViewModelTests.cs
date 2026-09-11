using System.Windows.Media;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.ViewModels.CharacterIcons;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class CharacterIconPickerViewModelTests
{
    [Fact]
    public void Picker_exposes_all_twenty_built_in_icons_in_catalog_order()
    {
        var icons = BuiltInCharacterIconIds.Ordered.Select(CreateIcon).ToArray();
        var viewModel = new CharacterIconPickerViewModel(icons, currentIconId: null);

        Assert.Equal(20, viewModel.BuiltInIcons.Count);
        Assert.Equal(BuiltInCharacterIconIds.Ordered, viewModel.BuiltInIcons.Select(icon => icon.IconId));
    }

    [Fact]
    public void Picker_preserves_catalog_order_and_existing_selection()
    {
        var icons = BuiltInCharacterIconIds.Ordered
            .Select(CreateIcon)
            .ToArray();

        var viewModel = new CharacterIconPickerViewModel(icons, "default-female-04");

        Assert.Equal(BuiltInCharacterIconIds.Ordered, viewModel.Icons.Select(icon => icon.IconId));
        Assert.Equal("default-female-04", viewModel.SelectedIcon!.IconId);
        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public void Picker_requires_an_explicit_selection_when_character_has_no_icon()
    {
        var viewModel = new CharacterIconPickerViewModel(
            [CreateIcon("default-male-01")],
            currentIconId: null);

        Assert.Null(viewModel.SelectedIcon);
        Assert.False(viewModel.CanApply);

        viewModel.SelectedIcon = viewModel.Icons[0];

        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public void Picker_preserves_built_in_grid_and_highlights_current_custom_icon()
    {
        var builtIns = BuiltInCharacterIconIds.Ordered.Select(CreateIcon).ToArray();
        var custom = new CustomCharacterIcon
        {
            Id = "custom-" + new string('b', 64),
            ImageSource = new DrawingImage()
        };

        var viewModel = new CharacterIconPickerViewModel(
            builtIns,
            [custom],
            CharacterIconReference.Custom(custom.Id),
            new FakeCustomIconService(custom));

        Assert.Equal(20, viewModel.BuiltInIcons.Count);
        Assert.Equal(BuiltInCharacterIconIds.Ordered, viewModel.BuiltInIcons.Select(icon => icon.IconId));
        Assert.Single(viewModel.CustomIcons);
        Assert.True(viewModel.HasCustomIcons);
        Assert.Equal(CharacterIconKind.Custom, viewModel.SelectedIcon!.Reference.Kind);
        Assert.Same(viewModel.CustomIcons[0], viewModel.SelectedCustomIcon);
        Assert.Null(viewModel.SelectedBuiltInIcon);
    }

    [Fact]
    public void Successful_import_is_added_once_and_selected_for_apply()
    {
        var viewModel = new CharacterIconPickerViewModel(
            [CreateIcon("default-male-01")],
            [],
            null,
            new FakeCustomIconService());
        var custom = new CustomCharacterIcon
        {
            Id = "custom-" + new string('c', 64),
            ImageSource = new DrawingImage()
        };

        viewModel.AddAndSelectCustomIcon(custom);
        viewModel.AddAndSelectCustomIcon(custom);

        Assert.Single(viewModel.CustomIcons);
        Assert.True(viewModel.HasCustomIcons);
        Assert.Same(viewModel.CustomIcons[0], viewModel.SelectedIcon);
        Assert.True(viewModel.CanApply);
    }

    [Fact]
    public void Failed_custom_icon_save_does_not_change_the_current_selection()
    {
        var builtIn = CreateIcon("default-male-01");
        var viewModel = new CharacterIconPickerViewModel(
            [builtIn],
            [],
            CharacterIconReference.BuiltIn(builtIn.Id),
            new FailingCustomIconService());

        var saved = viewModel.TrySaveAndSelectCustomIcon([1, 2, 3], out var errorMessage);

        Assert.False(saved);
        Assert.Equal("Disk unavailable.", errorMessage);
        Assert.Empty(viewModel.CustomIcons);
        Assert.Equal(builtIn.Id, viewModel.SelectedIcon!.IconId);
        Assert.Equal(CharacterIconKind.BuiltIn, viewModel.SelectedIcon.Reference.Kind);
    }

    [Fact]
    public void Unassigned_custom_icon_can_be_deleted_from_storage_and_picker()
    {
        var custom = new CustomCharacterIcon
        {
            Id = "custom-" + new string('d', 64),
            ImageSource = new DrawingImage()
        };
        var service = new FakeCustomIconService(custom);
        var viewModel = new CharacterIconPickerViewModel(
            [CreateIcon("default-male-01")],
            [custom],
            null,
            service);
        viewModel.SelectedCustomIcon = viewModel.CustomIcons[0];

        var deleted = viewModel.TryDeleteSelectedCustomIcon(out var errorMessage);

        Assert.True(deleted, errorMessage);
        Assert.Equal([custom.Id], service.DeletedIds);
        Assert.Empty(viewModel.CustomIcons);
        Assert.False(viewModel.HasCustomIcons);
        Assert.Null(viewModel.SelectedIcon);
        Assert.False(viewModel.CanDeleteSelectedCustomIcon);
    }

    [Fact]
    public void Assigned_custom_icon_is_blocked_from_deletion()
    {
        var custom = new CustomCharacterIcon
        {
            Id = "custom-" + new string('e', 64),
            ImageSource = new DrawingImage()
        };
        var service = new FakeCustomIconService(custom);
        var viewModel = new CharacterIconPickerViewModel(
            [CreateIcon("default-male-01")],
            [custom],
            CharacterIconReference.Custom(custom.Id),
            service,
            _ => ["Alpha Hero"]);

        Assert.Equal(["Alpha Hero"], viewModel.GetSelectedCustomIconAssignments());
        Assert.False(viewModel.TryDeleteSelectedCustomIcon(out var errorMessage));
        Assert.Contains("still assigned", errorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(service.DeletedIds);
        Assert.Single(viewModel.CustomIcons);
    }

    private static BuiltInCharacterIcon CreateIcon(string id) =>
        new()
        {
            Id = id,
            ImageSource = new DrawingImage()
        };

    private sealed class FakeCustomIconService(params CustomCharacterIcon[] icons)
        : ICustomCharacterIconService
    {
        public List<string> DeletedIds { get; } = [];

        public IReadOnlyList<CustomCharacterIcon> GetIcons() => icons;

        public bool TryGetIcon(string iconId, out CustomCharacterIcon icon)
        {
            icon = icons.FirstOrDefault(candidate => candidate.Id == iconId)!;
            return icon is not null;
        }

        public CustomCharacterIconSaveResult SaveNormalizedIcon(byte[] pngData) =>
            throw new NotSupportedException();

        public CustomCharacterIconDeleteResult DeleteIcon(string iconId)
        {
            DeletedIds.Add(iconId);
            return CustomCharacterIconDeleteResult.Success();
        }
    }

    private sealed class FailingCustomIconService : ICustomCharacterIconService
    {
        public IReadOnlyList<CustomCharacterIcon> GetIcons() => [];

        public bool TryGetIcon(string iconId, out CustomCharacterIcon icon)
        {
            icon = null!;
            return false;
        }

        public CustomCharacterIconSaveResult SaveNormalizedIcon(byte[] pngData) =>
            CustomCharacterIconSaveResult.Failure("Disk unavailable.");

        public CustomCharacterIconDeleteResult DeleteIcon(string iconId) =>
            CustomCharacterIconDeleteResult.Failure("Disk unavailable.");
    }
}

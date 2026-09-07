using System.Windows.Media.Imaging;
using CoHAnalytics.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.CharacterIcons;

public sealed partial class CustomCharacterIconEditorViewModel : ObservableObject
{
    public const double ViewportSize = 420;

    public CustomCharacterIconEditorViewModel(CustomCharacterIconSource source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        var fitScale = Math.Max(
            ViewportSize / source.Image.PixelWidth,
            ViewportSize / source.Image.PixelHeight);
        SourceDisplayWidth = source.Image.PixelWidth * fitScale;
        SourceDisplayHeight = source.Image.PixelHeight * fitScale;
    }

    public CustomCharacterIconSource Source { get; }

    public BitmapSource SourceImage => Source.Image;

    public double SourceDisplayWidth { get; }

    public double SourceDisplayHeight { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Scale))]
    [NotifyPropertyChangedFor(nameof(ScalePercentage))]
    private double _scaleSliderValue;

    public double Scale => Math.Pow(2, ScaleSliderValue);

    public string ScalePercentage => $"{Scale:P0}";

    [ObservableProperty]
    private double _offsetX;

    [ObservableProperty]
    private double _offsetY;

    [RelayCommand]
    private void Reset()
    {
        ScaleSliderValue = 0;
        OffsetX = 0;
        OffsetY = 0;
    }

    public void MoveBy(double horizontalChange, double verticalChange)
    {
        OffsetX = Math.Clamp(OffsetX + horizontalChange, -ViewportSize, ViewportSize);
        OffsetY = Math.Clamp(OffsetY + verticalChange, -ViewportSize, ViewportSize);
    }

    public byte[] RenderNormalizedPng() =>
        CustomCharacterIconImageProcessor.RenderNormalizedPng(
            Source.Image,
            Scale,
            OffsetX,
            OffsetY,
            ViewportSize);
}

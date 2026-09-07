using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Converters;

/// <summary>Resolves a theme brush resource key (for example <c>Brush.InspirationDamage</c>).</summary>
public sealed class BrushResourceKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var resourceKey = value as string;
        if (!string.IsNullOrWhiteSpace(resourceKey)
            && Application.Current?.TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }

        return Application.Current?.TryFindResource(ReferenceRarityPresentation.DefaultBrushKey) as Brush
            ?? Brushes.White;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

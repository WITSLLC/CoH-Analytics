using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Converters;

public sealed class ReferenceRarityBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var resourceKey = ReferenceRarityPresentation.ResolveBrushResourceKey(value as string);
        if (Application.Current?.TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }

        return Application.Current?.TryFindResource(ReferenceRarityPresentation.DefaultBrushKey) as Brush
            ?? Brushes.White;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CoHAnalytics.Converters;

/// <summary>Keeps a nested workspace scroller within the shell viewport.</summary>
public sealed class ViewportHeightOffsetConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double viewportHeight
            || parameter is not string rawOffset
            || !double.TryParse(rawOffset, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset))
        {
            return DependencyProperty.UnsetValue;
        }

        return Math.Max(280d, viewportHeight - offset);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

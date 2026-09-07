using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CoHAnalytics.Converters;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public static InverseBooleanToVisibilityConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is true;
        return visible ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

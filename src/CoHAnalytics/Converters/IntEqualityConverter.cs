using System.Globalization;
using System.Windows.Data;

namespace CoHAnalytics.Converters;

public sealed class IntEqualityConverter : IMultiValueConverter
{
    public static IntEqualityConverter Instance { get; } = new();

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return false;
        }

        return values[0] is int left && values[1] is int right && left == right;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

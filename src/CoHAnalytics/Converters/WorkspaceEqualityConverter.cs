using System.Globalization;
using System.Windows.Data;
using CoHAnalytics.Navigation;

namespace CoHAnalytics.Converters;

public sealed class WorkspaceEqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return false;
        }

        return values[0] is WorkspaceId selected && values[1] is WorkspaceId current && selected == current;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

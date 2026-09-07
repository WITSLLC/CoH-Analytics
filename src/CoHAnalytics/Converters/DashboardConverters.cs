using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CoHAnalytics.Navigation;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Converters;

public sealed class WorkspaceIdToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is WorkspaceId current && parameter is string raw && Enum.TryParse(raw, out WorkspaceId expected) && current == expected;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StatusKindToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (Application.Current is null)
        {
            return null;
        }

        return value switch
        {
            DashboardStatusKind.Success => Application.Current.FindResource("Brush.Success"),
            DashboardStatusKind.Warning => Application.Current.FindResource("Brush.Warning"),
            DashboardStatusKind.Error => Application.Current.FindResource("Brush.Error"),
            _ => Application.Current.FindResource("Brush.TextMuted")
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StatusKindToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var path = value switch
        {
            DashboardStatusKind.Success => "Assets/Images/Status/checkmark.png",
            DashboardStatusKind.Warning => "Assets/Images/Status/warning.png",
            DashboardStatusKind.Error => "Assets/Images/Status/error.png",
            _ => "Assets/Images/Status/information.png"
        };

        return new Uri($"pack://application:,,,/{path}", UriKind.Absolute);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ResourcePathToUriConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new Uri($"pack://application:,,,/{path.Replace('\\', '/')}", UriKind.Absolute);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class WorkspaceTemplateSelector : DataTemplateSelector
{
    public DataTemplate? DashboardTemplate { get; set; }

    public DataTemplate? AccountsTemplate { get; set; }

    public DataTemplate? LiveSessionTemplate { get; set; }

    public DataTemplate? DiagnosticsTemplate { get; set; }

    public DataTemplate? AnalyticsTemplate { get; set; }

    public DataTemplate? InternalToolsTemplate { get; set; }

    public DataTemplate? SettingsTemplate { get; set; }

    public DataTemplate? PlaceholderTemplate { get; set; }

    public DataTemplate? ReferenceTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        return item switch
        {
            DashboardViewModel => DashboardTemplate,
            AccountsViewModel => AccountsTemplate,
            LiveSessionViewModel => LiveSessionTemplate,
            DiagnosticsViewModel => DiagnosticsTemplate,
            AnalyticsViewModel => AnalyticsTemplate,
            InternalToolsViewModel => InternalToolsTemplate,
            SettingsViewModel => SettingsTemplate,
            ReferenceViewModel => ReferenceTemplate,
            PlaceholderWorkspaceViewModel => PlaceholderTemplate,
            _ => PlaceholderTemplate
        };
    }
}

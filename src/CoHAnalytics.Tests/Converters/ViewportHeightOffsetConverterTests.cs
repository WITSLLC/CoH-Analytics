using System.Globalization;
using System.Windows;
using CoHAnalytics.Converters;

namespace CoHAnalytics.Tests.Converters;

public sealed class ViewportHeightOffsetConverterTests
{
    private readonly ViewportHeightOffsetConverter _converter = new();

    [Fact]
    public void Convert_subtracts_the_requested_shell_offset()
    {
        var result = _converter.Convert(850d, typeof(double), "420", CultureInfo.InvariantCulture);

        Assert.Equal(430d, result);
    }

    [Fact]
    public void Convert_keeps_the_additional_section_scroller_usable_in_short_viewports()
    {
        var result = _converter.Convert(500d, typeof(double), "420", CultureInfo.InvariantCulture);

        Assert.Equal(280d, result);
    }

    [Fact]
    public void Convert_rejects_invalid_inputs()
    {
        var result = _converter.Convert("850", typeof(double), "420", CultureInfo.InvariantCulture);

        Assert.Same(DependencyProperty.UnsetValue, result);
    }
}

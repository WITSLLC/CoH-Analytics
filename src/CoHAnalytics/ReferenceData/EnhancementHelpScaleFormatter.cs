using System.Globalization;

namespace CoHAnalytics.ReferenceData;

internal static class EnhancementHelpScaleFormatter
{
    internal static string FormatDisplayPercentage(double rawPercentage)
    {
        var rounded = Math.Round(rawPercentage, 1, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded - Math.Truncate(rounded)) < 0.0000001)
        {
            return rounded.ToString("0", CultureInfo.InvariantCulture);
        }

        return rounded.ToString("0.0", CultureInfo.InvariantCulture);
    }
}

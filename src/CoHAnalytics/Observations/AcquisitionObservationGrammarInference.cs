using System.Text.RegularExpressions;
using CoHAnalytics.Services;

namespace CoHAnalytics.Observations;

internal static partial class AcquisitionObservationGrammarInference
{
    public static string InferGrammarSource(string observedText)
    {
        if (string.IsNullOrWhiteSpace(observedText))
        {
            return AcquisitionGrammarSource.ReceivedSimple;
        }

        var trimmed = observedText.Trim();
        if (GameplayRewardCurrencyGrammar.TryParseStandaloneMeritBody(trimmed, out _, out _))
        {
            return AcquisitionGrammarSource.ReceivedMeritVariant;
        }

        if (UnitsOfCurrencyPattern().IsMatch(trimmed))
        {
            return AcquisitionGrammarSource.ReceivedUnits;
        }

        return AcquisitionGrammarSource.ReceivedSimple;
    }

    [GeneratedRegex(@"^(?<qty>(?:\d{1,3}(?:,\d{3})*|\d+)) units of (?<name>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex UnitsOfCurrencyPattern();
}

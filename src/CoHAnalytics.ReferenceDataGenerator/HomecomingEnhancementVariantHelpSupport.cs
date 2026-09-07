using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingEnhancementVariantHelpSupport
{
    internal static string? ResolveVariantDisplayHelp(
        HomecomingMessageStore messages,
        HomecomingBoostDiscoveryRecord discovery,
        string catalogItemId)
    {
        if (string.IsNullOrWhiteSpace(discovery.DisplayHelpMessageKey))
        {
            return null;
        }

        if (!messages.TryResolve(discovery.DisplayHelpMessageKey, out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            throw new HomecomingEnhancementPromotionException(
                $"Enhancement '{catalogItemId}' display_help message '{discovery.DisplayHelpMessageKey}' is unresolved.");
        }

        return value;
    }

    internal static string? ResolveVariantShortHelp(
        HomecomingMessageStore messages,
        HomecomingBoostDiscoveryRecord discovery)
    {
        if (string.IsNullOrWhiteSpace(discovery.ShortHelpMessageKey)
            || !messages.TryResolve(discovery.ShortHelpMessageKey, out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value;
    }

    internal static string? ResolveLogicalHelpFromVariantTexts(IReadOnlyList<string?> texts)
    {
        if (texts.Count == 0)
        {
            return null;
        }

        var distinct = texts.Distinct(StringComparer.Ordinal).ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }

    internal static bool HasHelpDisagreement(IReadOnlyList<string?> texts) =>
        texts.Count > 0 && texts.Distinct(StringComparer.Ordinal).Count() > 1;
}

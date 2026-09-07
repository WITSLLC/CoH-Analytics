namespace CoHAnalytics.ReferenceData;

internal static class EnhancementVariantHelpValidation
{
    internal static bool TryValidateLogicalHelpConsistency(
        ItemReferenceRecord item,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (item.Family != ReferenceItemFamily.Enhancement || item.SourceVariants.Count == 0)
        {
            return true;
        }

        var displayHelps = item.SourceVariants
            .Select(value => value.DisplayHelp)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (item.DisplayHelp is not null
            && (displayHelps.Length != 1 || !string.Equals(displayHelps[0], item.DisplayHelp, StringComparison.Ordinal)))
        {
            failureReason =
                $"Enhancement '{item.CatalogItemId}' logical DisplayHelp disagrees with sourceVariants.";
            return false;
        }

        var shortHelps = item.SourceVariants
            .Select(value => value.ShortHelp)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (item.ShortHelp is not null
            && (shortHelps.Length != 1 || !string.Equals(shortHelps[0], item.ShortHelp, StringComparison.Ordinal)))
        {
            failureReason =
                $"Enhancement '{item.CatalogItemId}' logical ShortHelp disagrees with sourceVariants.";
            return false;
        }

        return true;
    }
}

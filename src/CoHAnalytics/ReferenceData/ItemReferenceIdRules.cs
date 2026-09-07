using System.Text.RegularExpressions;

namespace CoHAnalytics.ReferenceData;

internal static partial class ItemReferenceIdRules
{
    private static readonly IReadOnlyDictionary<ReferenceItemFamily, string> FamilyPrefixes =
        new Dictionary<ReferenceItemFamily, string>
        {
            [ReferenceItemFamily.Salvage] = "SAL-",
            [ReferenceItemFamily.Recipe] = "REC-",
            [ReferenceItemFamily.Enhancement] = "ENH-",
            [ReferenceItemFamily.EnhancementSet] = "SET-",
            [ReferenceItemFamily.Inspiration] = "INS-",
            [ReferenceItemFamily.RewardCurrency] = "CUR-",
            [ReferenceItemFamily.Badge] = "BAD-"
        };

    public static bool TryValidateItemId(string catalogItemId, ReferenceItemFamily family, out string? failureReason)
    {
        failureReason = null;

        if (string.IsNullOrWhiteSpace(catalogItemId))
        {
            failureReason = "CatalogItemId is required.";
            return false;
        }

        var trimmed = catalogItemId.Trim();
        if (!trimmed.Equals(catalogItemId, StringComparison.Ordinal))
        {
            failureReason = $"CatalogItemId '{catalogItemId}' must not include leading or trailing whitespace.";
            return false;
        }

        if (!FamilyPrefixes.TryGetValue(family, out var prefix))
        {
            failureReason = $"Unsupported family '{family}'.";
            return false;
        }

        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
        {
            failureReason = $"CatalogItemId '{trimmed}' must start with '{prefix}' for family '{family}'.";
            return false;
        }

        if (!CanonicalIdPattern().IsMatch(trimmed))
        {
            failureReason = $"CatalogItemId '{trimmed}' does not match the required SAL-/REC-/ENH-/SET-/INS-/CUR-/BAD- format.";
            return false;
        }

        return true;
    }

    public static bool TryValidateEnhancementSetId(string catalogItemId, out string? failureReason)
    {
        return TryValidateItemId(catalogItemId, ReferenceItemFamily.EnhancementSet, out failureReason);
    }

    [GeneratedRegex(@"^(SAL|REC|ENH|SET|INS|CUR|BAD)-\d{5}$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalIdPattern();
}

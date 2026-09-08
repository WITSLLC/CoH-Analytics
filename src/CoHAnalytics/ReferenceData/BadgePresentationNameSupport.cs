using System.Text.RegularExpressions;

namespace CoHAnalytics.ReferenceData;

/// <summary>Builds alignment- and gender-neutral badge names without inferring character state.</summary>
internal static partial class BadgePresentationNameSupport
{
    public static string GetNeutralDisplayName(
        IItemReferenceCatalog catalog,
        BadgeReferenceRecord badge)
    {
        if (catalog.TryGetById(badge.CatalogItemId, out var item)
            && !ContainsGenderExpression(item.CurrentDisplayName))
        {
            return item.CurrentDisplayName;
        }

        var names = ExpandGenderExpressions(badge.HeroName)
            .Concat(ExpandGenderExpressions(badge.VillainName))
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (names.Length > 0)
        {
            return string.Join(" / ", names);
        }

        return catalog.TryGetById(badge.CatalogItemId, out item)
            ? item.CurrentDisplayName
            : badge.HeroName;
    }

    internal static IReadOnlyList<string> ExpandGenderExpressions(string value)
    {
        var expanded = new List<string> { value };
        while (true)
        {
            var changed = false;
            var next = new List<string>();
            foreach (var candidate in expanded)
            {
                var match = GenderExpression().Match(candidate);
                if (!match.Success)
                {
                    next.Add(candidate);
                    continue;
                }

                changed = true;
                var prefix = candidate[..match.Index];
                var suffix = candidate[(match.Index + match.Length)..];
                next.Add(prefix + match.Groups["first"].Value + suffix);
                next.Add(prefix + match.Groups["second"].Value + suffix);
            }

            expanded = next;
            if (!changed)
            {
                return expanded;
            }
        }
    }

    private static bool ContainsGenderExpression(string value) =>
        value.Contains("{Hero.gender=", StringComparison.Ordinal);

    [GeneratedRegex(@"\{Hero\.gender=male (?<first>[^|{}]+)\|(?<second>[^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex GenderExpression();
}

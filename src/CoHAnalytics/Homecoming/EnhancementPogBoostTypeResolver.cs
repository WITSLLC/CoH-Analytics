namespace CoHAnalytics.Homecoming;

/// <summary>
/// Resolves the canonical Homecoming POG aspect from powers.bin <c>boosts_allowed</c> evidence.
/// </summary>
public static class EnhancementPogBoostTypeResolver
{
    /// <summary>
    /// Returns the first non-origin BOOST_TYPE in authored <c>boosts_allowed</c> order.
    /// </summary>
    public static string? TryResolvePrimaryBoostType(IReadOnlyList<string>? boostsAllowed)
    {
        if (boostsAllowed is null || boostsAllowed.Count == 0)
        {
            return null;
        }

        foreach (var boostType in boostsAllowed)
        {
            if (string.IsNullOrWhiteSpace(boostType))
            {
                continue;
            }

            if (IsOriginType(boostType))
            {
                continue;
            }

            return boostType;
        }

        return null;
    }

    private static bool IsOriginType(string boostType) =>
        boostType is "Science" or "Mutation" or "Magic" or "Technology" or "Natural";
}

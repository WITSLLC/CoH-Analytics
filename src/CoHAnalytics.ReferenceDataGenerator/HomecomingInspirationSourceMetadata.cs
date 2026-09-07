namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingInspirationSourceMetadata
{
    internal static string ParseHomecomingCategory(string homecomingSourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homecomingSourceId);
        if (!homecomingSourceId.StartsWith("Inspirations.", StringComparison.Ordinal))
        {
            throw new HomecomingPowersException(
                $"Inspiration source ID '{homecomingSourceId}' is not under Inspirations.");
        }

        var remainder = homecomingSourceId["Inspirations.".Length..];
        var lastSeparator = remainder.LastIndexOf('.');
        if (lastSeparator <= 0 || lastSeparator >= remainder.Length - 1)
        {
            throw new HomecomingPowersException(
                $"Inspiration source ID '{homecomingSourceId}' does not contain a category segment.");
        }

        return remainder[..lastSeparator];
    }

    internal static string? ParseStandardTier(string homecomingCategory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homecomingCategory);

        if (homecomingCategory.StartsWith("Small", StringComparison.Ordinal))
        {
            return "Small";
        }

        if (homecomingCategory.StartsWith("Medium", StringComparison.Ordinal))
        {
            return "Medium";
        }

        if (homecomingCategory.StartsWith("Large", StringComparison.Ordinal))
        {
            return "Large";
        }

        if (homecomingCategory.StartsWith("Super", StringComparison.Ordinal))
        {
            return "Super";
        }

        if (homecomingCategory is "Special" or "Holiday" or "Anniversary")
        {
            return null;
        }

        return null;
    }

    internal static string ParseInspirationForm(string homecomingCategory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homecomingCategory);

        if (homecomingCategory is "Special" or "Holiday" or "Anniversary")
        {
            return "Special/Event";
        }

        if (homecomingCategory.Contains("_Team_Dual", StringComparison.Ordinal))
        {
            return "TeamDual";
        }

        if (homecomingCategory.Contains("_Team", StringComparison.Ordinal))
        {
            return "Team";
        }

        if (homecomingCategory.Contains("_Dual", StringComparison.Ordinal))
        {
            return "Dual";
        }

        return "Single";
    }
}

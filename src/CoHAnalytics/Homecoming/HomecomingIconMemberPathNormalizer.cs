namespace CoHAnalytics.Homecoming;

internal static class HomecomingIconMemberPathNormalizer
{
    internal static IReadOnlyList<string> CreateMemberPathCandidates(string iconIdentity)
    {
        var baseName = NormalizeBaseName(iconIdentity);
        if (baseName.Length == 0)
        {
            return [];
        }

        return
        [
            $"texture_library/GUI/Icons/Enhancements/{baseName}.texture",
            $"texture_library/gui/icons/enhancements/{baseName.ToLowerInvariant()}.texture",
            $"texture_library/GUI/Icons/Badges/{baseName}.texture",
            $"texture_library/gui/icons/badges/{baseName.ToLowerInvariant()}.texture",
            $"texture_library/GUI/Icons/Inspirations/{baseName}.texture",
            $"texture_library/gui/icons/inspirations/{baseName.ToLowerInvariant()}.texture"
        ];
    }

    internal static string NormalizeBaseName(string iconIdentity)
    {
        if (string.IsNullOrWhiteSpace(iconIdentity))
        {
            return string.Empty;
        }

        var trimmed = iconIdentity.Trim();
        if (trimmed.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[..^4];
        }

        return trimmed;
    }
}

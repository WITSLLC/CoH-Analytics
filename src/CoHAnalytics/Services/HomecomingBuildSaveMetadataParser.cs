using System.Text.RegularExpressions;
using CoHAnalytics.Homecoming;

namespace CoHAnalytics.Services;

/// <summary>
/// Parses Homecoming <c>/buildsavefile</c> text exports for character display metadata.
/// </summary>
/// <remarks>
/// <para>
/// Game exports use a compact line-oriented format (see Mids Reborn <c>ImportFromBuildsave</c>).
/// Mids plain-text recovery sections may include explicit Primary/Secondary lines. Compressed
/// <c>|MBD;</c> / <c>|MxDz;</c> chunks are not decoded in this slice.
/// </para>
/// <para>
/// Current in-game build number is selected via <c>/select_build</c> and is not present in
/// buildsave file content.
/// </para>
/// </remarks>
public static class HomecomingBuildSaveMetadataParser
{
    private static readonly Regex GameHeaderRegex =
        new(@"^([^\:]+)\: Level ([0-9]+) ([a-zA-Z]+) ([a-zA-Z_]+)$", RegexOptions.CultureInvariant);

    private static readonly Regex MidsCharacterInfoRegex =
        new(@"^(.+)\: Level ([0-9]{1,2}) ([a-zA-Z]+) ([a-zA-Z ]+)$", RegexOptions.CultureInvariant);

    private static readonly Regex MidsCharacterInfoNamelessRegex =
        new(@"^Level ([0-9]{1,2}) ([a-zA-Z]+) ([a-zA-Z ]+)$", RegexOptions.CultureInvariant);

    private static readonly Regex ExplicitPowersetRegex =
        new(@"^(Primary|Secondary) Power Set\: (.+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex GamePowerLineRegex =
        new(@"^Level ([0-9]+)\: (.+)$", RegexOptions.CultureInvariant);

    public static bool TryParse(string content, out HomecomingBuildSaveMetadata metadata) =>
        TryParse(content, null, out metadata);

    public static bool TryParse(
        string content,
        IHomecomingArchetypePowerReferenceCatalog? powerCatalog,
        out HomecomingBuildSaveMetadata metadata)
    {
        metadata = null!;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (content.Contains("|MBD;", StringComparison.Ordinal)
            || content.Contains("|MxDz;", StringComparison.Ordinal))
        {
            return false;
        }

        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');

        string? characterName = null;
        string? archetype = null;
        string? primary = null;
        string? secondary = null;
        string? archetypePrefix = null;
        Dictionary<string, Dictionary<string, int>> votesByCategory = new(StringComparer.OrdinalIgnoreCase);
        List<string> categoryOrder = [];

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var explicitPowerset = ExplicitPowersetRegex.Match(line);
            if (explicitPowerset.Success)
            {
                var label = explicitPowerset.Groups[1].Value;
                var value = explicitPowerset.Groups[2].Value.Trim();
                if (string.Equals(label, "Primary", StringComparison.OrdinalIgnoreCase))
                {
                    primary = value;
                }
                else
                {
                    secondary = value;
                }

                continue;
            }

            var gameHeader = GameHeaderRegex.Match(line);
            if (gameHeader.Success)
            {
                characterName = gameHeader.Groups[1].Value.Trim();
                archetype = FormatArchetype(gameHeader.Groups[4].Value);
                archetypePrefix = NormalizeArchetypePrefix(archetype);
                continue;
            }

            var midsHeader = MidsCharacterInfoRegex.Match(line);
            if (midsHeader.Success)
            {
                characterName = midsHeader.Groups[1].Value.Trim();
                archetype = midsHeader.Groups[4].Value.Trim();
                archetypePrefix = NormalizeArchetypePrefix(archetype);
                continue;
            }

            var midsHeaderNameless = MidsCharacterInfoNamelessRegex.Match(line);
            if (midsHeaderNameless.Success)
            {
                archetype = midsHeaderNameless.Groups[3].Value.Trim();
                archetypePrefix = NormalizeArchetypePrefix(archetype);
                continue;
            }

            var powerLine = GamePowerLineRegex.Match(line);
            if (!powerLine.Success)
            {
                continue;
            }

            var powerTokens = powerLine.Groups[2].Value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (powerTokens.Length < 2)
            {
                continue;
            }

            if (!TryResolveArchetypePowerVote(
                    powerTokens,
                    archetypePrefix,
                    powerCatalog,
                    out var categoryGroup,
                    out var powersetDisplay))
            {
                continue;
            }

            if (!votesByCategory.TryGetValue(categoryGroup, out var categoryVotes))
            {
                categoryVotes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                votesByCategory[categoryGroup] = categoryVotes;
                categoryOrder.Add(categoryGroup);
            }

            categoryVotes.TryGetValue(powersetDisplay, out var count);
            categoryVotes[powersetDisplay] = count + 1;
        }

        if (primary is null || secondary is null)
        {
            AssignPowersetVotes(votesByCategory, categoryOrder, ref primary, ref secondary);
        }

        if (archetype is null)
        {
            return false;
        }

        metadata = new HomecomingBuildSaveMetadata
        {
            CharacterName = characterName,
            Archetype = archetype,
            PrimaryPowerSet = primary,
            SecondaryPowerSet = secondary,
            CurrentBuildNumber = null
        };

        return true;
    }

    private static bool TryResolveArchetypePowerVote(
        IReadOnlyList<string> powerTokens,
        string? archetypePrefix,
        IHomecomingArchetypePowerReferenceCatalog? powerCatalog,
        out string categoryGroup,
        out string powersetDisplay)
    {
        categoryGroup = string.Empty;
        powersetDisplay = string.Empty;

        var categoryToken = powerTokens[0];
        if (IsExcludedPowerCategory(categoryToken))
        {
            return false;
        }

        if (archetypePrefix is not null
            && !StartsWithArchetypePrefix(categoryToken, archetypePrefix))
        {
            return false;
        }

        string? powersetToken = null;
        string powerName;
        if (powerTokens.Count >= 3)
        {
            powersetToken = powerTokens[1];
            powerName = powerTokens[2];
        }
        else
        {
            powerName = powerTokens[1];
        }

        if (powerCatalog is not null
            && powerCatalog.TryResolvePower(categoryToken, powersetToken, powerName, out var resolution))
        {
            categoryGroup = resolution.CategoryGroup;
            powersetDisplay = resolution.PowersetDisplayName;
            return !HomecomingPowersetDisplayNames.IsGenericCategoryDisplayName(powersetDisplay);
        }

        if (powerTokens.Count >= 3)
        {
            categoryGroup = categoryToken;
            powersetDisplay = HomecomingPowersetDisplayNames.Format(powersetToken!);
            return !HomecomingPowersetDisplayNames.IsGenericCategoryDisplayName(powersetDisplay);
        }

        var remainder = StripArchetypePrefix(categoryToken, archetypePrefix);
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        if (HomecomingPowersetDisplayNames.IsGenericCategorySuffix(remainder))
        {
            return false;
        }

        categoryGroup = categoryToken;
        powersetDisplay = HomecomingPowersetDisplayNames.Format(remainder);
        return !HomecomingPowersetDisplayNames.IsGenericCategoryDisplayName(powersetDisplay);
    }

    private static void AssignPowersetVotes(
        IReadOnlyDictionary<string, Dictionary<string, int>> votesByCategory,
        IReadOnlyList<string> categoryOrder,
        ref string? primary,
        ref string? secondary)
    {
        foreach (var categoryGroup in categoryOrder)
        {
            if (!votesByCategory.TryGetValue(categoryGroup, out var categoryVotes))
            {
                continue;
            }

            var winner = categoryVotes
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(winner.Key))
            {
                continue;
            }

            if (primary is null)
            {
                primary = winner.Key;
                continue;
            }

            if (secondary is null && !string.Equals(primary, winner.Key, StringComparison.OrdinalIgnoreCase))
            {
                secondary = winner.Key;
                break;
            }
        }
    }

    private static bool IsExcludedPowerCategory(string categoryToken)
    {
        if (string.IsNullOrWhiteSpace(categoryToken))
        {
            return true;
        }

        return categoryToken.StartsWith("Inherent", StringComparison.OrdinalIgnoreCase)
            || categoryToken.StartsWith("Pool", StringComparison.OrdinalIgnoreCase)
            || categoryToken.StartsWith("Epic", StringComparison.OrdinalIgnoreCase)
            || categoryToken.StartsWith("Incarnate", StringComparison.OrdinalIgnoreCase)
            || categoryToken.StartsWith("Prestige", StringComparison.OrdinalIgnoreCase)
            || categoryToken.StartsWith("Temp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithArchetypePrefix(string categoryToken, string archetypePrefix) =>
        categoryToken.StartsWith(archetypePrefix + "_", StringComparison.OrdinalIgnoreCase)
        || categoryToken.StartsWith(archetypePrefix, StringComparison.OrdinalIgnoreCase);

    private static string StripArchetypePrefix(string categoryToken, string? archetypePrefix)
    {
        if (archetypePrefix is null)
        {
            return categoryToken;
        }

        if (categoryToken.StartsWith(archetypePrefix + "_", StringComparison.OrdinalIgnoreCase))
        {
            return categoryToken[(archetypePrefix.Length + 1)..];
        }

        if (categoryToken.StartsWith(archetypePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return categoryToken[archetypePrefix.Length..].TrimStart('_');
        }

        return categoryToken;
    }

    private static string FormatArchetype(string rawClassToken)
    {
        var token = rawClassToken.Trim();
        if (token.StartsWith("Class_", StringComparison.OrdinalIgnoreCase))
        {
            token = token["Class_".Length..];
        }

        return HumanizeToken(token);
    }

    private static string? NormalizeArchetypePrefix(string? archetype)
    {
        if (string.IsNullOrWhiteSpace(archetype))
        {
            return null;
        }

        return archetype
            .Replace(' ', '_')
            .Replace('-', '_')
            .ToLowerInvariant();
    }

    private static string HumanizeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var parts = token.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(
            ' ',
            parts.Select(part => part.Length switch
            {
                0 => string.Empty,
                1 => part.ToUpperInvariant(),
                _ => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()
            }));
    }

    /// <summary>
    /// Extracts Homecoming badge source IDs from the <c>Badges Earned:</c> section of a
    /// <c>/buildsavefile</c> export. Returns <see langword="false"/> when the section header
    /// is absent; an empty list means the section exists but lists no IDs.
    /// </summary>
    public static bool TryParseBadgeSourceIds(string content, out IReadOnlyList<string> sourceIds)
    {
        sourceIds = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var collecting = false;
        var collected = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!collecting)
            {
                if (string.Equals(line, "Badges Earned:", StringComparison.Ordinal))
                {
                    collecting = true;
                }

                continue;
            }

            if (line.Length == 0 || IsSectionDelimiter(line))
            {
                if (collected.Count > 0 && IsSectionDelimiter(line))
                {
                    break;
                }

                continue;
            }

            if (IsSectionHeader(line))
            {
                break;
            }

            collected.Add(line);
        }

        if (!collecting)
        {
            return false;
        }

        sourceIds = collected;
        return true;
    }

    private static bool IsSectionDelimiter(string line) =>
        line.Length > 0 && line.All(ch => ch == '-');

    private static bool IsSectionHeader(string line) =>
        line.EndsWith(':') && line.Contains(' ', StringComparison.Ordinal);
}

public sealed record HomecomingBuildSaveMetadata
{
    public string? CharacterName { get; init; }

    public required string Archetype { get; init; }

    public string? PrimaryPowerSet { get; init; }

    public string? SecondaryPowerSet { get; init; }

    public int? CurrentBuildNumber { get; init; }
}

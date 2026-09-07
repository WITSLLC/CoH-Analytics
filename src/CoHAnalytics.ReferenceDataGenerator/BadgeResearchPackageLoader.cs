using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class BadgeResearchPackageLoader
{
    private const string ExplorationInventoryFileName = "01 - Exploration Badge Inventory.md";
    private const string HistoryPlaqueInventoryFileName = "02 - History Plaque Inventory.md";
    private const string ZoneIndexFileName = "03 - Zone Index.md";
    private const string MasterCatalogRelativePath =
        "Badge and Accolade Master Inventory/07 - Master Badge Catalog.md";
    private const string AccoladeRequirementsRelativePath =
        "Badge and Accolade Master Inventory/08 - Accolade Requirements.md";
    private const string DependencyCrosswalkRelativePath =
        "Badge and Accolade Master Inventory/09 - Badge-Accolade Dependency Crosswalk.md";
    private const string TitleCrosswalkRelativePath =
        "Badge and Accolade Master Inventory/10 - Title and Internal Name Crosswalk.md";

    internal static BadgeResearchPackage Load(string researchRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(researchRoot);
        var root = Path.GetFullPath(researchRoot);
        if (!Directory.Exists(root))
        {
            throw new BadgeResearchPackageException($"Research root '{root}' was not found.");
        }

        var explorationText = ReadRequired(root, ExplorationInventoryFileName);
        var plaqueText = ReadRequired(root, HistoryPlaqueInventoryFileName);
        var zoneText = ReadRequired(root, ZoneIndexFileName);
        var masterText = ReadRequired(root, MasterCatalogRelativePath);
        var accoladeText = ReadRequired(root, AccoladeRequirementsRelativePath);
        var crosswalkText = ReadRequired(root, TitleCrosswalkRelativePath);
        var dependencyText = TryRead(root, DependencyCrosswalkRelativePath);

        return new BadgeResearchPackage(
            ParseExplorationZoneSets(explorationText),
            ParseExplorationLocations(explorationText),
            ParseHistoryPlaques(plaqueText),
            ParseZoneIndex(zoneText),
            ParseMasterCatalog(masterText),
            ParseAccoladeIndex(accoladeText),
            ParseAccoladeDetails(accoladeText),
            ParseTitleCrosswalk(crosswalkText),
            dependencyText is null ? [] : ParseDependencyCrosswalk(dependencyText));
    }

    private static string ReadRequired(string root, string relativePath)
    {
        var fullPath = Path.Combine(root, relativePath);
        if (!File.Exists(fullPath))
        {
            throw new BadgeResearchPackageException($"Research file '{fullPath}' was not found.");
        }

        return File.ReadAllText(fullPath);
    }

    private static string? TryRead(string root, string relativePath)
    {
        var fullPath = Path.Combine(root, relativePath);
        return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
    }

    private static IReadOnlyList<BadgeResearchZoneSetRow> ParseExplorationZoneSets(string markdown)
    {
        var section = ExtractSection(markdown, "## Exploration badge set inventory");
        var rows = new List<BadgeResearchZoneSetRow>();
        foreach (var cells in ParseMarkdownTables(section))
        {
            if (cells.Count < 4 || string.Equals(cells[0], "Set / zone", StringComparison.Ordinal))
            {
                continue;
            }

            rows.Add(new BadgeResearchZoneSetRow(
                cells[0],
                cells[1],
                ParseInt(cells[2]),
                cells[3],
                cells.Count > 4 ? cells[4] : string.Empty));
        }

        return rows;
    }

    private static IReadOnlyList<BadgeResearchExplorationLocationRow> ParseExplorationLocations(string markdown)
    {
        var section = ExtractSection(markdown, "## Badge records by zone/location");
        var rows = new List<BadgeResearchExplorationLocationRow>();
        string? currentZone = null;
        foreach (var line in section.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                currentZone = trimmed["### ".Length..].Trim();
                continue;
            }

            if (currentZone is null || !trimmed.StartsWith("|", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = SplitTableRow(trimmed);
            if (cells.Count < 11
                || string.Equals(cells[0], "Badge", StringComparison.Ordinal)
                || cells[0].All(static character => character is '-' or ':' or ' '))
            {
                continue;
            }

            rows.Add(new BadgeResearchExplorationLocationRow(
                currentZone,
                cells[0],
                cells[1],
                ParseNullableDouble(cells[2]),
                ParseNullableDouble(cells[3]),
                ParseNullableDouble(cells[4]),
                cells[5],
                cells[6],
                cells[7],
                cells[8],
                cells[9],
                cells[10]));
        }

        return rows;
    }

    private static IReadOnlyList<BadgeResearchPlaqueRow> ParseHistoryPlaques(string markdown)
    {
        var rows = new List<BadgeResearchPlaqueRow>();
        string? currentCollection = null;
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal)
                && !trimmed.Contains("Inventory", StringComparison.OrdinalIgnoreCase))
            {
                currentCollection = trimmed["## ".Length..].Trim();
                continue;
            }

            if (currentCollection is null || !trimmed.StartsWith("|", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = SplitTableRow(trimmed);
            if (cells.Count < 12 || string.Equals(cells[0], "Plaque name", StringComparison.Ordinal))
            {
                continue;
            }

            rows.Add(new BadgeResearchPlaqueRow(
                currentCollection,
                cells[0],
                cells[1],
                ParseNullableDouble(cells[2]),
                ParseNullableDouble(cells[3]),
                ParseNullableDouble(cells[4]),
                cells[5],
                cells[6],
                ParseInt(cells[7]),
                cells[8],
                cells[9],
                cells[10],
                cells[11]));
        }

        return rows;
    }

    private static IReadOnlyList<BadgeResearchZoneIndexEntry> ParseZoneIndex(string markdown)
    {
        var entries = new List<BadgeResearchZoneIndexEntry>();
        string? currentZone = null;
        string? alignment = null;
        string? levelRange = null;
        string? zoneType = null;
        int badgeCount = 0;
        int plaqueCount = 0;
        var explorationCompletion = new List<string>();
        var historyCompletion = new List<string>();
        var explorationBadges = new List<string>();
        var plaques = new List<string>();

        void Flush()
        {
            if (currentZone is null)
            {
                return;
            }

            entries.Add(new BadgeResearchZoneIndexEntry(
                currentZone,
                alignment,
                levelRange,
                zoneType,
                badgeCount,
                plaqueCount,
                explorationCompletion,
                historyCompletion,
                explorationBadges,
                plaques));
            currentZone = null;
            alignment = null;
            levelRange = null;
            zoneType = null;
            badgeCount = 0;
            plaqueCount = 0;
            explorationCompletion.Clear();
            historyCompletion.Clear();
            explorationBadges.Clear();
            plaques.Clear();
        }

        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                currentZone = trimmed["## ".Length..].Trim();
                continue;
            }

            if (currentZone is null || !trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            var content = trimmed[2..];
            var separator = content.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var key = content[..separator].Trim();
            var value = content[(separator + 1)..].Trim();
            switch (key)
            {
                case "Alignment/access":
                    alignment = value;
                    break;
                case "Level range (reference only)":
                    levelRange = value;
                    break;
                case "Zone type":
                    zoneType = value;
                    break;
                case "Badge count":
                    badgeCount = ParseInt(value);
                    break;
                case "Plaque count":
                    plaqueCount = ParseInt(value);
                    break;
                case "Exploration completion badge(s)":
                    explorationCompletion.AddRange(SplitList(value));
                    break;
                case "History completion badge(s)":
                    historyCompletion.AddRange(SplitList(value));
                    break;
                case "Exploration badges":
                    explorationBadges.AddRange(SplitList(value));
                    break;
                case "Plaques":
                    plaques.AddRange(SplitList(value));
                    break;
            }
        }

        Flush();
        return entries;
    }

    private static IReadOnlyList<BadgeResearchMasterCatalogRow> ParseMasterCatalog(string markdown)
    {
        var rows = new List<BadgeResearchMasterCatalogRow>();
        string? currentCategory = null;
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                currentCategory = trimmed["### ".Length..].Trim();
                continue;
            }

            if (currentCategory is null || !trimmed.StartsWith("|", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = SplitTableRow(trimmed);
            if (cells.Count < 8 || string.Equals(cells[0], "Reference ID", StringComparison.Ordinal))
            {
                continue;
            }

            if (!uint.TryParse(cells[1], NumberStyles.None, CultureInfo.InvariantCulture, out var setTitleId))
            {
                continue;
            }

            rows.Add(new BadgeResearchMasterCatalogRow(
                cells[0],
                setTitleId,
                cells[2],
                cells[3],
                cells[4],
                cells[5],
                cells[6],
                cells[7],
                currentCategory));
        }

        return rows;
    }

    private static IReadOnlyList<BadgeResearchAccoladeIndexRow> ParseAccoladeIndex(string markdown)
    {
        var section = ExtractSection(markdown, "## Accolade index");
        var rows = new List<BadgeResearchAccoladeIndexRow>();
        foreach (var cells in ParseMarkdownTables(section))
        {
            if (cells.Count < 5 || string.Equals(cells[0], "SetTitle", StringComparison.Ordinal))
            {
                continue;
            }

            if (!uint.TryParse(cells[0], NumberStyles.None, CultureInfo.InvariantCulture, out var setTitleId))
            {
                continue;
            }

            rows.Add(new BadgeResearchAccoladeIndexRow(
                setTitleId,
                cells[1],
                ParseInt(cells[2]),
                cells[3],
                cells[4]));
        }

        return rows;
    }

    private static IReadOnlyDictionary<uint, BadgeResearchAccoladeDetail> ParseAccoladeDetails(string markdown)
    {
        var section = ExtractSection(markdown, "## Detailed requirements");
        var details = new Dictionary<uint, BadgeResearchAccoladeDetail>();
        var blocks = Regex.Split(section, @"\r?\n(?=### )");
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block))
            {
                continue;
            }

            var lines = block.Split('\n');
            var title = lines[0].Trim();
            if (!title.StartsWith("### ", StringComparison.Ordinal))
            {
                continue;
            }

            uint? setTitleId = null;
            string? internalName = null;
            string? displayTitles = null;
            string? rewardPower = null;
            string verification = "Unverified";
            string? requirementLogic = null;
            string? requirementSynopsis = null;
            string? requirementText = null;
            var prerequisites = new List<string>();
            foreach (var rawLine in lines.Skip(1))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("- SetTitle ID:", StringComparison.Ordinal))
                {
                    var value = line["- SetTitle ID:".Length..].Trim().Trim('`');
                    _ = uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed);
                    setTitleId = parsed;
                    continue;
                }

                if (line.StartsWith("- Internal name:", StringComparison.Ordinal))
                {
                    internalName = Unquote(line["- Internal name:".Length..].Trim());
                    continue;
                }

                if (line.StartsWith("- Display titles:", StringComparison.Ordinal))
                {
                    displayTitles = line["- Display titles:".Length..].Trim();
                    continue;
                }

                if (line.StartsWith("- Reward/power:", StringComparison.Ordinal))
                {
                    rewardPower = line["- Reward/power:".Length..].Trim();
                    continue;
                }

                if (line.StartsWith("- Verification:", StringComparison.Ordinal))
                {
                    verification = NormalizeVerification(line["- Verification:".Length..].Trim());
                    continue;
                }

                if (line.StartsWith("- Requirement logic:", StringComparison.Ordinal))
                {
                    requirementLogic = line["- Requirement logic:".Length..].Trim();
                    continue;
                }

                if (line.StartsWith("- Requirement synopsis:", StringComparison.Ordinal))
                {
                    requirementSynopsis = line["- Requirement synopsis:".Length..].Trim();
                    continue;
                }

                if (line.StartsWith("- Requirement:", StringComparison.Ordinal))
                {
                    requirementText = line["- Requirement:".Length..].Trim();
                    continue;
                }

                if (line.StartsWith("- Linked prerequisites:", StringComparison.Ordinal))
                {
                    continue;
                }

                var prerequisiteMatch = Regex.Match(line, @"^\s*-\s*\[(.+?)\]\(");
                if (prerequisiteMatch.Success)
                {
                    prerequisites.Add(prerequisiteMatch.Groups[1].Value.Trim());
                }
            }

            if (setTitleId is null || internalName is null)
            {
                continue;
            }

            details[setTitleId.Value] = new BadgeResearchAccoladeDetail(
                setTitleId.Value,
                internalName,
                displayTitles ?? title["### ".Length..].Trim(),
                rewardPower,
                verification,
                requirementLogic,
                requirementSynopsis,
                requirementText,
                prerequisites,
                ResolveRequirementLogicPattern(requirementLogic, requirementText, prerequisites));
        }

        return details;
    }

    private static IReadOnlyList<BadgeResearchTitleCrosswalkRow> ParseTitleCrosswalk(string markdown)
    {
        var section = ExtractSection(markdown, "## Complete crosswalk");
        var rows = new List<BadgeResearchTitleCrosswalkRow>();
        foreach (var cells in ParseMarkdownTables(section))
        {
            if (cells.Count < 10 || string.Equals(cells[0], "SetTitle", StringComparison.Ordinal))
            {
                continue;
            }

            if (!uint.TryParse(cells[0], NumberStyles.None, CultureInfo.InvariantCulture, out var setTitleId))
            {
                continue;
            }

            rows.Add(new BadgeResearchTitleCrosswalkRow(
                setTitleId,
                Unquote(cells[1]),
                cells[2],
                cells[3],
                cells[4],
                cells[5],
                cells[6],
                cells[7],
                cells[8],
                cells[9]));
        }

        return rows;
    }

    private static IReadOnlyList<BadgeResearchDependencyCrosswalkRow> ParseDependencyCrosswalk(string markdown)
    {
        var section = ExtractSection(markdown, "## Badge-to-Accolade Dependency Crosswalk");
        if (section.Length == 0)
        {
            section = markdown;
        }

        var rows = new List<BadgeResearchDependencyCrosswalkRow>();
        foreach (var cells in ParseMarkdownTables(section))
        {
            if (cells.Count < 4 || string.Equals(cells[0], "SetTitle", StringComparison.Ordinal))
            {
                continue;
            }

            if (!uint.TryParse(cells[0], NumberStyles.None, CultureInfo.InvariantCulture, out var setTitleId))
            {
                continue;
            }

            var accolades = cells[3]
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ExtractMarkdownLinkText)
                .Where(value => value.Length > 0)
                .ToArray();
            rows.Add(new BadgeResearchDependencyCrosswalkRow(
                setTitleId,
                cells[1],
                cells[2],
                accolades));
        }

        return rows;
    }

    private static ReferenceRequirementLogicPattern ResolveRequirementLogicPattern(
        string? requirementLogic,
        string? requirementText,
        IReadOnlyList<string> prerequisites)
    {
        if (prerequisites.Count == 0)
        {
            return ReferenceRequirementLogicPattern.TextOnly;
        }

        if (!string.IsNullOrWhiteSpace(requirementLogic))
        {
            if (requirementLogic.Contains("no alternate-method phrase was detected", StringComparison.OrdinalIgnoreCase))
            {
                return ReferenceRequirementLogicPattern.And;
            }

            if (requirementLogic.Contains("Complex/alignment-sensitive", StringComparison.OrdinalIgnoreCase))
            {
                return ReferenceRequirementLogicPattern.And;
            }
        }

        if (ContainsExplicitOrRequirementLanguage(requirementLogic, requirementText))
        {
            return ReferenceRequirementLogicPattern.Or;
        }

        return ReferenceRequirementLogicPattern.And;
    }

    private static bool ContainsExplicitOrRequirementLanguage(string? requirementLogic, string? requirementText)
    {
        foreach (var source in new[] { requirementLogic, requirementText })
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            if (source.Contains("either of two methods", StringComparison.OrdinalIgnoreCase)
                || source.Contains("any one of the following", StringComparison.OrdinalIgnoreCase)
                || source.Contains("one of the following", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (source.Contains("alternate method", StringComparison.OrdinalIgnoreCase)
                && !source.Contains("no alternate-method phrase was detected", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<IReadOnlyList<string>> ParseMarkdownTables(string markdown)
    {
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("|", StringComparison.Ordinal)
                || trimmed.Contains("---", StringComparison.Ordinal))
            {
                continue;
            }

            yield return SplitTableRow(trimmed);
        }
    }

    private static IReadOnlyList<string> SplitTableRow(string row)
    {
        var cells = row.Trim('|').Split('|');
        return cells.Select(cell => cell.Trim()).ToArray();
    }

    private static string ExtractSection(string markdown, string heading)
    {
        var start = markdown.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        start += heading.Length;
        var next = markdown.IndexOf("\n## ", start, StringComparison.Ordinal);
        return next < 0 ? markdown[start..] : markdown[start..next];
    }

    private static IReadOnlyList<string> SplitList(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("None", StringComparison.OrdinalIgnoreCase)
            || value.Equals("None identified", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(part => part.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToArray();
    }

    private static int ParseInt(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    private static double? ParseNullableDouble(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0
            || trimmed.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("-", StringComparison.Ordinal))
        {
            return null;
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('`') && trimmed.EndsWith('`') && trimmed.Length >= 2)
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static string NormalizeVerification(string value)
    {
        var trimmed = value.Trim().Trim('*');
        if (trimmed.StartsWith("Verified", StringComparison.OrdinalIgnoreCase))
        {
            return "Verified";
        }

        if (trimmed.StartsWith("Likely Correct", StringComparison.OrdinalIgnoreCase))
        {
            return "Likely Correct";
        }

        return trimmed;
    }

    private static string ExtractMarkdownLinkText(string value)
    {
        var match = Regex.Match(value, @"\[(.+?)\]");
        return match.Success ? match.Groups[1].Value.Trim() : value.Trim();
    }
}

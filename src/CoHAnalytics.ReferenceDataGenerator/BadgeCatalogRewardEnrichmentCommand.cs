using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class BadgeCatalogRewardEnrichmentCommand
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (!TryParseArgs(args, out var catalogPath, out var researchPath, out var failureReason))
        {
            error.WriteLine(failureReason);
            error.WriteLine(
                "Usage: enrich-badge-rewards --catalog <item-catalog.v1.json> --research <ReferenceDataRoot>");
            return 1;
        }

        try
        {
            var research = BadgeResearchPackageLoader.Load(researchPath);
            var explorationCompletionNames = BadgeRewardTextSupport.BuildExplorationZoneCompletionDisplayNames(
                research.ExplorationZoneSets.Select(zone => zone.CompletionBadges));

            using var stream = File.OpenRead(catalogPath);
            var document = JsonSerializer.Deserialize<ItemReferenceCatalogDocument>(stream, ReadOptions)
                ?? throw new InvalidOperationException("Catalog document is empty.");

            var enrichedCount = 0;
            foreach (var badge in document.Badges)
            {
                if (string.IsNullOrWhiteSpace(badge.HomecomingSourceId)
                    || string.IsNullOrWhiteSpace(badge.HeroName))
                {
                    continue;
                }

                research.AccoladeDetails.TryGetValue(badge.SetTitleId ?? 0, out var detail);
                var rewardText = BadgeRewardTextSupport.ResolveRewardText(
                    badge.HomecomingSourceId,
                    badge.HeroName,
                    badge.VillainName,
                    detail?.RewardPower,
                    explorationCompletionNames,
                    badge.HeroDescription,
                    badge.VillainDescription);

                if (string.IsNullOrWhiteSpace(rewardText))
                {
                    badge.RewardText = null;
                    continue;
                }

                if (!string.Equals(badge.RewardText, rewardText, StringComparison.Ordinal))
                {
                    enrichedCount++;
                }

                badge.RewardText = rewardText;
            }

            var json = JsonSerializer.Serialize(document, WriteOptions);
            var tempPath = catalogPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, catalogPath, overwrite: true);
            output.WriteLine($"Enriched badge rewards in '{catalogPath}'. Updated {enrichedCount} badge reward entries.");
            return 0;
        }
        catch (Exception exception) when (
            exception is BadgeResearchPackageException
                or IOException
                or JsonException
                or InvalidOperationException)
        {
            error.WriteLine($"Badge reward enrichment: FAIL — {exception.Message}");
            return 1;
        }
    }

    private static bool TryParseArgs(
        string[] args,
        out string catalogPath,
        out string researchPath,
        out string failureReason)
    {
        catalogPath = string.Empty;
        researchPath = string.Empty;
        failureReason = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--catalog" when index + 1 < args.Length:
                    catalogPath = args[++index];
                    break;
                case "--research" when index + 1 < args.Length:
                    researchPath = args[++index];
                    break;
                default:
                    failureReason = $"Unknown or incomplete argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(catalogPath) || string.IsNullOrWhiteSpace(researchPath))
        {
            failureReason = "Both --catalog and --research are required.";
            return false;
        }

        if (!File.Exists(catalogPath))
        {
            failureReason = $"Catalog file '{catalogPath}' was not found.";
            return false;
        }

        if (!Directory.Exists(researchPath))
        {
            failureReason = $"Research root '{researchPath}' was not found.";
            return false;
        }

        return true;
    }
}

using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingBadgeLogReceiptAliasSyncCommand
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
                "Usage: sync-badge-log-receipt-aliases --catalog <item-catalog.v1.json> --research <ReferenceDataRoot>");
            return 1;
        }

        try
        {
            var catalogFullPath = Path.GetFullPath(catalogPath);
            if (!File.Exists(catalogFullPath))
            {
                throw new HomecomingBadgePromotionException($"Catalog path '{catalogFullPath}' was not found.");
            }

            var document = JsonSerializer.Deserialize<ItemReferenceCatalogDocument>(
                File.ReadAllBytes(catalogFullPath),
                ReadOptions)
                ?? throw new HomecomingBadgePromotionException("Catalog document is empty.");

            var research = BadgeResearchPackageLoader.Load(researchPath);
            var builtAliases = HomecomingBadgePromotionSupport.BuildLogReceiptAliasesFromCatalogBadges(
                document,
                research);
            var added = HomecomingBadgePromotionSupport.MergeBuiltLogReceiptAliases(
                document,
                builtAliases);

            var serialized = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, WriteOptions) + "\n");
            using (var validationStream = new MemoryStream(serialized))
            {
                var load = ItemReferenceCatalogLoader.Load(validationStream);
                if (!load.Succeeded)
                {
                    throw new HomecomingBadgePromotionException(
                        $"Synced catalog failed validation: {load.FailureReason}");
                }
            }

            File.WriteAllBytes(catalogFullPath, serialized);
            output.WriteLine($"Synced badge log-receipt aliases: {added} added to '{catalogFullPath}'.");
            return 0;
        }
        catch (Exception exception) when (
            exception is HomecomingBadgePromotionException
                or BadgeResearchPackageException
                or IOException
                or JsonException)
        {
            error.WriteLine($"Badge log-receipt alias sync: FAIL — {exception.Message}");
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
            var option = args[index];
            if (option is "--catalog" && index + 1 < args.Length)
            {
                catalogPath = args[++index];
                continue;
            }

            if (option is "--research" && index + 1 < args.Length)
            {
                researchPath = args[++index];
                continue;
            }

            failureReason = $"Unknown argument '{option}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(catalogPath) || string.IsNullOrWhiteSpace(researchPath))
        {
            failureReason = "Both --catalog and --research are required.";
            return false;
        }

        return true;
    }
}

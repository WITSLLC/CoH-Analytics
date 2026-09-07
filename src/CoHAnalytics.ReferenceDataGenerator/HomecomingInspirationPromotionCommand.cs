using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingInspirationPromotionCommand
{
    internal const string CatalogVersion = "item-ref-3.1.0";
    internal const string SourceRevision = "homecoming-inspiration-promotion-2026-08-16";
    private const string SourceNotes =
        "Promote Homecoming Inspirations into the canonical catalog with client-derived metadata.";

    internal static readonly JsonSerializerOptions ReadOptions = new()
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
        if (!TryParseArgs(args, out var candidatePath, out var catalogPath, out var failureReason))
        {
            error.WriteLine(failureReason);
            error.WriteLine(
                "Usage: promote-homecoming-inspirations --candidate <inspirations.json> --catalog <item-catalog.v1.json>");
            return 1;
        }

        try
        {
            var result = Promote(candidatePath, catalogPath);
            WriteSummary(output, result);
            return 0;
        }
        catch (Exception exception) when (
            exception is HomecomingInspirationPromotionException
                or HomecomingInspirationCandidateException
                or InvalidOperationException
                or IOException
                or JsonException
                or InvalidDataException)
        {
            error.WriteLine($"Inspiration promotion: FAIL — {exception.Message}");
            return 1;
        }
    }

    internal static HomecomingInspirationPromotionResult Promote(string candidatePath, string catalogPath)
    {
        var first = PromoteOnce(candidatePath, catalogPath);
        var second = PromoteOnce(candidatePath, catalogPath);
        if (!string.Equals(first.CatalogSha256, second.CatalogSha256, StringComparison.Ordinal))
        {
            throw new HomecomingInspirationPromotionException(
                "Inspiration promotion is not deterministic across repeated runs.");
        }

        return first with { DeterminismSha256 = second.CatalogSha256 };
    }

    private static HomecomingInspirationPromotionResult PromoteOnce(string candidatePath, string catalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);

        var candidate = HomecomingInspirationPromotionSupport.LoadCandidate(candidatePath);
        var catalogFullPath = Path.GetFullPath(catalogPath);
        if (!File.Exists(catalogFullPath))
        {
            throw new HomecomingInspirationPromotionException(
                $"Catalog path '{catalogFullPath}' was not found.");
        }

        var document = JsonSerializer.Deserialize<ItemReferenceCatalogDocument>(
            File.ReadAllBytes(catalogFullPath),
            ReadOptions)
            ?? throw new HomecomingInspirationPromotionException("Catalog document is empty.");

        var artifacts = HomecomingInspirationPromotionSupport.Apply(document, candidate);
        HomecomingPromotionManifestSupport.ApplyPromotionRelease(
            document,
            CatalogVersion,
            SourceRevision,
            SourceNotes,
            candidate.Source.BuildVersion);

        var serialized = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, WriteOptions) + "\n");
        using (var validationStream = new MemoryStream(serialized))
        {
            var load = ItemReferenceCatalogLoader.Load(validationStream);
            if (!load.Succeeded)
            {
                throw new HomecomingInspirationPromotionException(
                    $"Promoted catalog failed validation: {load.FailureReason}");
            }
        }

        File.WriteAllBytes(catalogFullPath, serialized);
        return new HomecomingInspirationPromotionResult(
            catalogFullPath,
            candidate.Source.BuildVersion,
            candidate.Source.PackageRevision,
            Convert.ToHexString(SHA256.HashData(serialized)),
            null,
            artifacts);
    }

    private static bool TryParseArgs(
        string[] args,
        out string candidatePath,
        out string catalogPath,
        out string failureReason)
    {
        candidatePath = string.Empty;
        catalogPath = string.Empty;
        failureReason = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (string.Equals(option, "--candidate", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    failureReason = "Missing value for --candidate.";
                    return false;
                }

                candidatePath = args[++index];
                continue;
            }

            if (string.Equals(option, "--catalog", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    failureReason = "Missing value for --catalog.";
                    return false;
                }

                catalogPath = args[++index];
                continue;
            }

            failureReason = $"Unknown promote-homecoming-inspirations option '{option}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            failureReason = "Missing required --candidate path.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(catalogPath))
        {
            failureReason = "Missing required --catalog path.";
            return false;
        }

        return true;
    }

    private static void WriteSummary(TextWriter output, HomecomingInspirationPromotionResult result)
    {
        output.WriteLine($"Catalog: {result.CatalogPath}");
        output.WriteLine($"Build: {result.BuildVersion}");
        output.WriteLine($"Package revision: {result.PackageRevision}");
        output.WriteLine($"Inspirations promoted: {result.Artifacts.PromotedCount}");
        output.WriteLine($"Existing IDs preserved: {result.Artifacts.ExistingCount}");
        output.WriteLine($"New Inspirations promoted: {result.Artifacts.NewCount}");
        output.WriteLine($"Catalog SHA-256: {result.CatalogSha256}");
        output.WriteLine("Inspiration promotion: PASS");
    }
}

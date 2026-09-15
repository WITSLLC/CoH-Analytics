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
        if (!TryParseArgs(
                args,
                out var candidatePath,
                out var catalogPath,
                out var allowProductionWrite,
                out var failureReason))
        {
            error.WriteLine(failureReason);
            error.WriteLine(
                "Usage: promote-homecoming-inspirations --candidate <inspirations.json> --catalog <item-catalog.v1.json> [--allow-production-write]");
            return 1;
        }

        try
        {
            var result = Promote(candidatePath, catalogPath, allowProductionWrite);
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

    internal static HomecomingInspirationPromotionResult Promote(
        string candidatePath,
        string catalogPath,
        bool allowProductionWrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        var catalogFullPath = Path.GetFullPath(catalogPath);
        if (!File.Exists(catalogFullPath))
        {
            throw new HomecomingInspirationPromotionException(
                $"Catalog path '{catalogFullPath}' was not found.");
        }

        var originalBytes = File.ReadAllBytes(catalogFullPath);
        var first = PromoteOnce(candidatePath, catalogFullPath, originalBytes);
        var second = PromoteOnce(candidatePath, catalogFullPath, originalBytes);
        if (!first.Serialized.AsSpan().SequenceEqual(second.Serialized))
        {
            throw new HomecomingInspirationPromotionException(
                "Inspiration promotion is not deterministic across repeated runs.");
        }

        var written = CatalogPromotionWriteGuard.CommitSerialized(
            catalogFullPath,
            originalBytes,
            first.Serialized,
            CatalogPromotionOwnership.Inspirations,
            allowProductionWrite);
        var sha256 = Convert.ToHexString(SHA256.HashData(written));
        return first.Result with
        {
            CatalogSha256 = sha256,
            DeterminismSha256 = sha256
        };
    }

    private static InspirationPromotionPass PromoteOnce(
        string candidatePath,
        string catalogFullPath,
        byte[] originalBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogFullPath);

        var candidate = HomecomingInspirationPromotionSupport.LoadCandidate(candidatePath);
        var document = JsonSerializer.Deserialize<ItemReferenceCatalogDocument>(
            originalBytes,
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
        return new InspirationPromotionPass(
            serialized,
            new HomecomingInspirationPromotionResult(
                catalogFullPath,
                candidate.Source.BuildVersion,
                candidate.Source.PackageRevision,
                Convert.ToHexString(SHA256.HashData(serialized)),
                null,
                artifacts));
    }

    private readonly record struct InspirationPromotionPass(
        byte[] Serialized,
        HomecomingInspirationPromotionResult Result);

    private static bool TryParseArgs(
        string[] args,
        out string candidatePath,
        out string catalogPath,
        out bool allowProductionWrite,
        out string failureReason)
    {
        candidatePath = string.Empty;
        catalogPath = string.Empty;
        allowProductionWrite = false;
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

            if (CatalogPromotionWriteGuard.IsAllowProductionWriteOption(option))
            {
                allowProductionWrite = true;
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

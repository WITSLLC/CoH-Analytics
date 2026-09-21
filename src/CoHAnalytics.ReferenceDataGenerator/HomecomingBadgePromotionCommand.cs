using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingBadgePromotionCommand
{
    internal const string CatalogVersion = "item-ref-3.0.0";
    internal const string SourceRevision = "homecoming-badge-promotion-2026-08-14";
    private const string SourceNotes =
        "Promote Homecoming badges and research package data into the canonical catalog.";
    private const string MessageMemberName = "bin/clientmessages-en.bin";
    private const string BadgesMemberName = "bin/badges.bin";

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
        if (!TryParseArgs(args, out var installRoot, out var catalogPath, out var researchPath, out var failureReason))
        {
            error.WriteLine(failureReason);
            error.WriteLine(
                "Usage: promote-homecoming-badges --install <HomecomingRoot> --catalog <item-catalog.v1.json> --research <ReferenceDataRoot>");
            return 1;
        }

        try
        {
            var beforeHashes = SnapshotHomecomingHashes(installRoot);
            var result = Promote(installRoot, catalogPath, researchPath);
            var afterHashes = SnapshotHomecomingHashes(installRoot);
            if (!beforeHashes.SequenceEqual(afterHashes, StringComparer.Ordinal))
            {
                throw new HomecomingBadgePromotionException(
                    "Homecoming installation changed during promotion.");
            }

            WriteSummary(output, result);
            return 0;
        }
        catch (Exception exception) when (
            exception is HomecomingBadgePromotionException
                or BadgeResearchPackageException
                or HomecomingStaticDataSourceException
                or HomecomingPiggException
                or HomecomingBadgeCandidateException
                or HomecomingBadgesException
                or HomecomingMessageStoreException
                or InvalidOperationException
                or IOException
                or JsonException
                or InvalidDataException)
        {
            error.WriteLine($"Badge promotion: FAIL — {exception.Message}");
            return 1;
        }
    }

    internal static HomecomingBadgePromotionResult Promote(
        string installRoot,
        string catalogPath,
        string researchPath)
    {
        var first = PromoteOnce(installRoot, catalogPath, researchPath);
        var second = PromoteOnce(installRoot, catalogPath, researchPath);
        if (!string.Equals(first.CatalogSha256, second.CatalogSha256, StringComparison.Ordinal))
        {
            throw new HomecomingBadgePromotionException(
                "Badge promotion is not deterministic across repeated runs.");
        }

        return first with { DeterminismSha256 = second.CatalogSha256 };
    }

    private static HomecomingBadgePromotionResult PromoteOnce(
        string installRoot,
        string catalogPath,
        string researchPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(researchPath);

        var source = HomecomingStaticDataSourceDiscovery.Discover(installRoot);
        var catalogFullPath = Path.GetFullPath(catalogPath);
        if (!File.Exists(catalogFullPath))
        {
            throw new HomecomingBadgePromotionException($"Catalog path '{catalogFullPath}' was not found.");
        }

        var document = JsonSerializer.Deserialize<ItemReferenceCatalogDocument>(
            File.ReadAllBytes(catalogFullPath),
            ReadOptions)
            ?? throw new HomecomingBadgePromotionException("Catalog document is empty.");

        var messages = HomecomingMessageStoreReader.Read(
            HomecomingPiggMemberReader.ReadMember(source.BinPiggPath, MessageMemberName));
        var badgeRecords = HomecomingBadgesReader.Read(
            HomecomingPiggMemberReader.ReadMember(source.BinPiggPath, BadgesMemberName));
        var badgeSourceById = document.Badges
            .Where(badge =>
                !string.IsNullOrWhiteSpace(badge.CatalogItemId)
                && !string.IsNullOrWhiteSpace(badge.HomecomingSourceId))
            .ToDictionary(
                badge => badge.CatalogItemId!,
                badge => badge.HomecomingSourceId!,
                StringComparer.Ordinal);
        var currentBadges = document.Items
            .Where(item =>
                string.Equals(item.Family, nameof(ReferenceItemFamily.Badge), StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(item.CatalogItemId)
                && !string.IsNullOrWhiteSpace(item.CurrentDisplayName))
            .Select(item => new CurrentBadgeIdentity(
                item.CatalogItemId!,
                item.CurrentDisplayName!,
                item.Subtype,
                badgeSourceById.GetValueOrDefault(item.CatalogItemId!)))
            .ToArray();
        var candidate = HomecomingBadgeCandidateGenerator.Create(
            badgeRecords,
            messages,
            currentBadges,
            source.BuildVersion,
            source.PackageRevision);

        if (candidate.Records.Any(record => record.MatchStatus == nameof(HomecomingBadgeMatchStatus.Ambiguous)))
        {
            throw new HomecomingBadgePromotionException(
                "Badge promotion refused ambiguous BAD reconciliations.");
        }

        var research = BadgeResearchPackageLoader.Load(researchPath);
        var artifacts = HomecomingBadgePromotionSupport.Build(candidate.Records, research);
        var stats = ApplyArtifacts(document, artifacts, research);

        HomecomingPromotionManifestSupport.ApplyPromotionRelease(
            document,
            CatalogVersion,
            SourceRevision,
            SourceNotes,
            source.BuildVersion);

        var serialized = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, WriteOptions) + "\n");
        using (var validationStream = new MemoryStream(serialized))
        {
            var load = ItemReferenceCatalogLoader.Load(validationStream);
            if (!load.Succeeded)
            {
                throw new HomecomingBadgePromotionException(
                    $"Promoted catalog failed validation: {load.FailureReason}");
            }
        }

        File.WriteAllBytes(catalogFullPath, serialized);
        return new HomecomingBadgePromotionResult(
            catalogFullPath,
            source.BuildVersion,
            Convert.ToHexString(SHA256.HashData(serialized)),
            Convert.ToHexString(SHA256.HashData(serialized)),
            candidate.Summary.PlayerFacingBadgeRecords,
            stats);
    }

    private static BadgePromotionStats ApplyArtifacts(
        ItemReferenceCatalogDocument document,
        HomecomingBadgePromotionArtifacts artifacts,
        BadgeResearchPackage research)
    {
        var itemDocumentsById = document.Items
            .Where(value => !string.IsNullOrWhiteSpace(value.CatalogItemId))
            .ToDictionary(value => value.CatalogItemId!, StringComparer.Ordinal);
        var aliases = document.Aliases ?? [];
        var aliasKeys = aliases
            .Select(alias => ItemReferenceLookup.NormalizeLookupKey(alias.Text ?? string.Empty))
            .Where(key => !string.IsNullOrEmpty(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stats = new BadgePromotionStats
        {
            ResearchCrosswalkRows = research.TitleCrosswalk.Count,
            ResearchExplorationLocations = research.ExplorationLocations.Count,
            ResearchPlaques = research.HistoryPlaques.Count,
            ResearchAccolades = research.AccoladeDetails.Count
        };

        foreach (var item in artifacts.Items)
        {
            if (itemDocumentsById.TryGetValue(item.CatalogItemId!, out var existing))
            {
                existing.Subtype = item.Subtype;
                existing.CurrentDisplayName = item.CurrentDisplayName;
                existing.Icon = item.Icon;
                existing.VerificationStatus = item.VerificationStatus;
                stats.BadgesUpdated++;
            }
            else
            {
                itemDocumentsById[item.CatalogItemId!] = item;
                stats.BadgesAdded++;
            }

            stats.BadgesPromoted++;
        }

        var promotedBadgeIds = artifacts.Badges
            .Select(badge => badge.CatalogItemId!)
            .ToHashSet(StringComparer.Ordinal);
        var staleBadgeItemIds = itemDocumentsById.Values
            .Where(item => string.Equals(item.Family, nameof(ReferenceItemFamily.Badge), StringComparison.OrdinalIgnoreCase)
                && !promotedBadgeIds.Contains(item.CatalogItemId!))
            .Select(item => item.CatalogItemId!)
            .ToArray();
        foreach (var staleId in staleBadgeItemIds)
        {
            if (itemDocumentsById.Remove(staleId))
            {
                stats.BadgesRemoved++;
            }
        }

        if (staleBadgeItemIds.Length > 0)
        {
            var staleSet = staleBadgeItemIds.ToHashSet(StringComparer.Ordinal);
            aliases = aliases
                .Where(alias => !staleSet.Contains(alias.CatalogItemId ?? string.Empty, StringComparer.Ordinal))
                .ToList();
        }

        aliases = aliases
            .Where(alias =>
                !promotedBadgeIds.Contains(alias.CatalogItemId ?? string.Empty, StringComparer.Ordinal)
                || !string.Equals(
                    alias.NameKind,
                    nameof(ReferenceAliasNameKind.LogReceipt),
                    StringComparison.Ordinal))
            .ToList();
        aliasKeys = aliases
            .Select(alias => ItemReferenceLookup.NormalizeLookupKey(alias.Text ?? string.Empty))
            .Where(key => !string.IsNullOrEmpty(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in artifacts.Aliases)
        {
            var lookupKey = ItemReferenceLookup.NormalizeLookupKey(alias.Text ?? string.Empty);
            if (string.IsNullOrEmpty(lookupKey) || !aliasKeys.Add(lookupKey))
            {
                continue;
            }

            aliases.Add(alias);
            stats.AliasesAdded++;
        }

        document.Items = itemDocumentsById.Values
            .OrderBy(value => value.CatalogItemId, StringComparer.Ordinal)
            .ToList();
        document.Aliases = aliases
            .OrderBy(value => value.CatalogItemId, StringComparer.Ordinal)
            .ThenBy(value => value.Text, StringComparer.Ordinal)
            .ToList();
        document.Badges = artifacts.Badges
            .OrderBy(value => value.CatalogItemId, StringComparer.Ordinal)
            .ToList();
        document.BadgeLocations = artifacts.BadgeLocations.ToList();
        document.Zones = artifacts.Zones.ToList();
        document.BadgeAccoladeRequirements = artifacts.BadgeAccoladeRequirements.ToList();

        stats.BadgeLocations = document.BadgeLocations.Count;
        stats.Zones = document.Zones.Count;
        stats.AccoladeRequirements = document.BadgeAccoladeRequirements.Count;
        foreach (var badge in document.Badges)
        {
            var kind = badge.ReferenceKind ?? nameof(ReferenceBadgeKind.Other);
            stats.ReferenceKindCounts[kind] = stats.ReferenceKindCounts.GetValueOrDefault(kind) + 1;
        }

        return stats;
    }

    private static bool TryParseArgs(
        string[] args,
        out string installRoot,
        out string catalogPath,
        out string researchPath,
        out string failureReason)
    {
        installRoot = string.Empty;
        catalogPath = string.Empty;
        researchPath = string.Empty;
        failureReason = string.Empty;
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (option is "--install" && index + 1 < args.Length)
            {
                installRoot = args[++index];
                continue;
            }

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

            failureReason = $"Unknown promote-homecoming-badges option '{option}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(installRoot)
            || string.IsNullOrWhiteSpace(catalogPath)
            || string.IsNullOrWhiteSpace(researchPath))
        {
            failureReason = "--install, --catalog, and --research are required.";
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> SnapshotHomecomingHashes(string installRoot)
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(installRoot);
        return
        [
            $"{source.BinPiggPath}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.BinPiggPath)))}"
        ];
    }

    private static void WriteSummary(TextWriter output, HomecomingBadgePromotionResult result)
    {
        output.WriteLine("Badge promotion: PASS");
        output.WriteLine($"Catalog: {result.CatalogPath}");
        output.WriteLine($"Homecoming build: {result.BuildVersion}");
        output.WriteLine($"Source badge rows: {result.SourceBadgeRows}");
        output.WriteLine($"Badges promoted: {result.Stats.BadgesPromoted}");
        output.WriteLine($"Badges added: {result.Stats.BadgesAdded}");
        output.WriteLine($"Badges updated: {result.Stats.BadgesUpdated}");
        output.WriteLine($"Badge locations: {result.Stats.BadgeLocations}");
        output.WriteLine($"Zones: {result.Stats.Zones}");
        output.WriteLine($"Accolade requirements: {result.Stats.AccoladeRequirements}");
        output.WriteLine($"Aliases added: {result.Stats.AliasesAdded}");
        output.WriteLine($"Research crosswalk rows: {result.Stats.ResearchCrosswalkRows}");
        output.WriteLine($"Research exploration locations: {result.Stats.ResearchExplorationLocations}");
        output.WriteLine($"Research plaques: {result.Stats.ResearchPlaques}");
        output.WriteLine($"Research accolades: {result.Stats.ResearchAccolades}");
        output.WriteLine(
            "Reference kind counts: " +
            string.Join(
                ", ",
                result.Stats.ReferenceKindCounts
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value}")));
        output.WriteLine($"Catalog SHA-256: {result.CatalogSha256}");
        output.WriteLine($"Determinism SHA-256: {result.DeterminismSha256}");
    }
}

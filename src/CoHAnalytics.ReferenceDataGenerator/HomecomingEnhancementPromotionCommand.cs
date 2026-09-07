using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingEnhancementPromotionCommand
{
    internal const string CatalogVersion = "item-ref-2.8.0";
    internal const string SourceRevision = "homecoming-enhancement-resolver-inputs-2026-08-12";
    private const string SourceNotes =
        "I1: promote canonical Enhancement help Scale resolver static facts (effects, boost flags, NamedTables).";

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
        if (!TryParseArgs(args, out var installRoot, out var catalogPath, out var failureReason))
        {
            error.WriteLine(failureReason);
            error.WriteLine(
                "Usage: promote-homecoming-enhancements --install <HomecomingRoot> --catalog <item-catalog.v1.json>");
            return 1;
        }

        try
        {
            var beforeHashes = SnapshotHomecomingHashes(installRoot);
            var result = Promote(installRoot, catalogPath);
            var afterHashes = SnapshotHomecomingHashes(installRoot);
            if (!beforeHashes.SequenceEqual(afterHashes, StringComparer.Ordinal))
            {
                throw new HomecomingEnhancementPromotionException(
                    "Homecoming installation changed during promotion.");
            }

            WriteSummary(output, result);
            return 0;
        }
        catch (Exception exception) when (
            exception is HomecomingEnhancementPromotionException
                or HomecomingStaticDataSourceException
                or HomecomingPiggException
                or HomecomingPowersBoostDiscoveryException
                or HomecomingBoostSetsDiscoveryException
                or HomecomingBoostSetsException
                or HomecomingPowersException
                or HomecomingEnhancementCandidateException
                or HomecomingMessageStoreException
                or InvalidOperationException
                or IOException
                or JsonException
                or InvalidDataException)
        {
            error.WriteLine($"Enhancement promotion: FAIL — {exception.Message}");
            return 1;
        }
    }

    internal static HomecomingEnhancementPromotionResult Promote(string installRoot, string catalogPath)
    {
        var sourceSnapshot = HomecomingEnhancementSourceSnapshot.Load(installRoot);
        return Promote(sourceSnapshot, catalogPath);
    }

    internal static HomecomingEnhancementPromotionResult Promote(
        HomecomingEnhancementSourceSnapshot sourceSnapshot,
        string catalogPath)
    {
        ArgumentNullException.ThrowIfNull(sourceSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);

        sourceSnapshot.EnsureSourceUnchanged();
        var catalogFullPath = Path.GetFullPath(catalogPath);
        if (!File.Exists(catalogFullPath))
        {
            throw new HomecomingEnhancementPromotionException(
                $"Catalog path '{catalogFullPath}' was not found.");
        }

        var startingCatalog = File.ReadAllBytes(catalogFullPath);
        var first = PromoteOnce(sourceSnapshot, catalogFullPath, startingCatalog);
        var second = PromoteOnce(sourceSnapshot, catalogFullPath, startingCatalog);
        if (!string.Equals(first.Result.CatalogSha256, second.Result.CatalogSha256, StringComparison.Ordinal)
            || !first.SerializedCatalog.AsSpan().SequenceEqual(second.SerializedCatalog))
        {
            throw new HomecomingEnhancementPromotionException(
                "Enhancement promotion is not deterministic across repeated runs.");
        }

        sourceSnapshot.EnsureSourceUnchanged();
        File.WriteAllBytes(catalogFullPath, first.SerializedCatalog);
        return first.Result with { DeterminismSha256 = second.Result.CatalogSha256 };
    }

    private static HomecomingEnhancementPromotionPassResult PromoteOnce(
        HomecomingEnhancementSourceSnapshot sourceSnapshot,
        string catalogFullPath,
        byte[] startingCatalog)
    {
        var document = JsonSerializer.Deserialize<ItemReferenceCatalogDocument>(
            startingCatalog,
            ReadOptions)
            ?? throw new HomecomingEnhancementPromotionException("Catalog document is empty.");

        var source = sourceSnapshot.Source;
        var messages = sourceSnapshot.Messages;
        var concreteBoosts = sourceSnapshot.ConcreteBoosts;
        var boostSets = sourceSnapshot.BoostSets;
        var discoveryBoosts = sourceSnapshot.DiscoveryBoosts;
        var powerDiscoveryBySourceId = sourceSnapshot.PowerDiscoveryBySourceId;
        var currentEnhancements = document.Items
            .Where(item =>
                string.Equals(item.Family, nameof(ReferenceItemFamily.Enhancement), StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(item.CatalogItemId)
                && !string.IsNullOrWhiteSpace(item.CurrentDisplayName))
            .Select(item => new CurrentEnhancementIdentity(
                item.CatalogItemId!,
                item.CurrentDisplayName!,
                item.EnhancementSetId,
                item.SourceVariants?
                    .Select(variant => variant.HomecomingSourceId)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray()
                ?? []))
            .ToArray();
        var currentSets = document.EnhancementSets
            .Where(set =>
                !string.IsNullOrWhiteSpace(set.CatalogItemId)
                && !string.IsNullOrWhiteSpace(set.CurrentDisplayName))
            .Select(set => new CurrentEnhancementSetIdentity(
                set.CatalogItemId!,
                set.CurrentDisplayName!))
            .ToArray();
        var candidate = HomecomingEnhancementCandidateGenerator.Create(
            concreteBoosts,
            boostSets,
            messages,
            discoveryBoosts,
            currentEnhancements,
            currentSets,
            source.BuildVersion,
            source.PackageRevision);

        if (candidate.EnhancementSets.Any(value =>
                value.MatchStatus == nameof(HomecomingEnhancementMatchStatus.Ambiguous))
            || candidate.Enhancements.Any(value =>
                value.MatchStatus == nameof(HomecomingEnhancementMatchStatus.Ambiguous)))
        {
            throw new HomecomingEnhancementPromotionException(
                "Enhancement promotion refused ambiguous ENH/SET reconciliations.");
        }

        var discoverySets = sourceSnapshot.DiscoverySets;

        var setDocumentsById = document.EnhancementSets
            .Where(value => !string.IsNullOrWhiteSpace(value.CatalogItemId))
            .ToDictionary(value => value.CatalogItemId!, StringComparer.Ordinal);
        var itemDocumentsById = document.Items
            .Where(value => !string.IsNullOrWhiteSpace(value.CatalogItemId))
            .ToDictionary(value => value.CatalogItemId!, StringComparer.Ordinal);

        var productionBoostSets = boostSets.ToDictionary(
            value => value.HomecomingSetId,
            StringComparer.Ordinal);
        var setEnhancementsByHomecomingSetId = candidate.Enhancements
            .Where(value => !string.IsNullOrWhiteSpace(value.HomecomingEnhancementSetId))
            .GroupBy(value => value.HomecomingEnhancementSetId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<HomecomingEnhancementCandidateRecord>)group
                    .OrderBy(value => value.AppOwnedId, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var stats = new PromotionStats();
        PromoteSets(
            candidate,
            discoverySets,
            productionBoostSets,
            setEnhancementsByHomecomingSetId,
            powerDiscoveryBySourceId,
            messages,
            setDocumentsById,
            stats);
        PromoteEnhancements(
            candidate,
            discoveryBoosts,
            messages,
            itemDocumentsById,
            setDocumentsById,
            stats);
        PromoteResolverFacts(
            sourceSnapshot,
            candidate,
            document,
            itemDocumentsById,
            setDocumentsById,
            stats);
        ApplyHomecomingServerAvailability(
            itemDocumentsById,
            setDocumentsById,
            candidate,
            stats);
        ReconcileEnhancementAliases(document, itemDocumentsById);

        document.EnhancementSets = setDocumentsById.Values
            .OrderBy(value => value.CatalogItemId, StringComparer.Ordinal)
            .ToList();
        document.Items = itemDocumentsById.Values
            .OrderBy(value => value.CatalogItemId, StringComparer.Ordinal)
            .ToList();
        document.Aliases = document.Aliases
            .OrderBy(value => value.CatalogItemId, StringComparer.Ordinal)
            .ThenBy(value => value.Text, StringComparer.Ordinal)
            .ToList();

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
                throw new HomecomingEnhancementPromotionException(
                    $"Promoted catalog failed validation: {load.FailureReason}");
            }
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(serialized));
        return new HomecomingEnhancementPromotionPassResult(
            new HomecomingEnhancementPromotionResult(
                catalogFullPath,
                source.BuildVersion,
                sha256,
                sha256,
                stats),
            serialized);
    }

    private static bool TryParseArgs(
        string[] args,
        out string installRoot,
        out string catalogPath,
        out string failureReason)
    {
        installRoot = string.Empty;
        catalogPath = string.Empty;
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

            failureReason = $"Unknown promote-homecoming-enhancements option '{option}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(installRoot) || string.IsNullOrWhiteSpace(catalogPath))
        {
            failureReason = "Both --install and --catalog are required.";
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> SnapshotHomecomingHashes(string installRoot)
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(installRoot);
        return
        [
            $"{source.BinPiggPath}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.BinPiggPath)))}",
            $"{source.BinPowersPiggPath}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.BinPowersPiggPath)))}"
        ];
    }

    private static void PromoteSets(
        HomecomingEnhancementCandidateDocument candidate,
        IReadOnlyDictionary<string, HomecomingBoostSetDiscoveryRecord> discoverySets,
        IReadOnlyDictionary<string, HomecomingBoostSetRecord> productionBoostSets,
        IReadOnlyDictionary<string, IReadOnlyList<HomecomingEnhancementCandidateRecord>> setEnhancementsByHomecomingSetId,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> powerDiscoveryBySourceId,
        HomecomingMessageStore messages,
        Dictionary<string, EnhancementSetReferenceRecordDocument> setDocumentsById,
        PromotionStats stats)
    {
        foreach (var setCandidate in candidate.EnhancementSets.OrderBy(
                     value => value.AppOwnedId, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(setCandidate.AppOwnedId))
            {
                throw new HomecomingEnhancementPromotionException(
                    $"Boost Set '{setCandidate.HomecomingSetId}' has no app-owned identity.");
            }

            if (!discoverySets.TryGetValue(setCandidate.HomecomingSetId, out var discoverySet))
            {
                throw new HomecomingEnhancementPromotionException(
                    $"Boost Set '{setCandidate.HomecomingSetId}' was missing from discovery parse.");
            }

            if (!setDocumentsById.TryGetValue(setCandidate.AppOwnedId, out var setDocument))
            {
                setDocument = new EnhancementSetReferenceRecordDocument
                {
                    CatalogItemId = setCandidate.AppOwnedId,
                    CurrentDisplayName = setCandidate.DisplayName,
                    ActiveStatus = nameof(ReferenceActiveStatus.Active),
                    VerificationStatus = nameof(ReferenceVerificationStatus.VerifiedMultiSource)
                };
                setDocumentsById[setCandidate.AppOwnedId] = setDocument;
                stats.SetsAdded++;
            }
            else
            {
                stats.SetsUpdated++;
            }

            var categoryCode = !string.IsNullOrWhiteSpace(setCandidate.CategoryCode)
                ? setCandidate.CategoryCode
                : discoverySet.CategoryCode;
            if (string.IsNullOrWhiteSpace(categoryCode))
            {
                throw new HomecomingEnhancementPromotionException(
                    $"Boost Set '{setCandidate.HomecomingSetId}' has no category code.");
            }

            messages.TryResolve(categoryCode, out var categoryMessage);
            setDocument.HomecomingSetId = setCandidate.HomecomingSetId;
            setDocument.CurrentDisplayName = setCandidate.DisplayName;
            setDocument.CategoryCode = categoryCode;
            setDocument.CategoryDisplayText = HomecomingPresentationLabels.ResolveCategoryDisplayText(
                categoryCode,
                categoryMessage);
            setDocument.RarityCode = setCandidate.RarityCode;
            setDocument.RarityDisplayText = HomecomingPresentationLabels.ResolveRarityDisplayText(
                setCandidate.RarityDisplayText);
            setDocument.MinimumLevel = checked((int)setCandidate.MinimumLevel);
            setDocument.MaximumLevel = checked((int)setCandidate.MaximumLevel);
            if (!productionBoostSets.TryGetValue(setCandidate.HomecomingSetId, out var productionSet))
            {
                throw new HomecomingEnhancementPromotionException(
                    $"Boost Set '{setCandidate.HomecomingSetId}' was missing from production parse.");
            }

            if (discoverySet.Bonuses.Count == 0 && productionSet.MemberGroups.Count > 0)
            {
                throw new HomecomingEnhancementPromotionException(
                    $"Boost Set '{setCandidate.HomecomingSetId}' has no parsed bonus tiers.");
            }

            setEnhancementsByHomecomingSetId.TryGetValue(
                setCandidate.HomecomingSetId,
                out var setEnhancements);
            setEnhancements ??= Array.Empty<HomecomingEnhancementCandidateRecord>();

            setDocument.Bonuses = discoverySet.Bonuses
                .Select(bonus =>
                {
                    var requiresPattern = HomecomingSetBonusRequiresSupport.ClassifyPattern(
                        bonus.RequiresTokens);
                    var requiredEnhancementIds = HomecomingSetBonusRequiresSupport.ResolveRequiredEnhancementIds(
                        setCandidate.HomecomingSetId,
                        setCandidate.AppOwnedId,
                        bonus.RequiresTokens,
                        productionSet.MemberGroups,
                        setEnhancements);

                    return new EnhancementSetBonusReferenceRecordDocument
                    {
                        MinimumBoosts = checked((int)bonus.MinimumBoosts),
                        MaximumBoosts = checked((int)bonus.MaximumBoosts),
                        RequiresPattern = requiresPattern.ToString(),
                        RequiresTokens = bonus.RequiresTokens.ToList(),
                        RequiredEnhancementIds = requiredEnhancementIds.ToList(),
                        AutoPowers = bonus.AutoPowerSourceIds
                            .Select(autoPowerId => new EnhancementSetBonusPowerReferenceRecordDocument
                            {
                                HomecomingSourceId = autoPowerId,
                                DisplayHelp = ResolveRequiredBonusAutoPowerHelp(
                                    powerDiscoveryBySourceId,
                                    messages,
                                    setCandidate.HomecomingSetId,
                                    autoPowerId)
                            })
                            .ToList()
                    };
                })
                .ToList();

            stats.BonusTiers += setDocument.Bonuses.Count;
            stats.BonusAutoPowers += setDocument.Bonuses.Sum(value => value.AutoPowers?.Count ?? 0);
            foreach (var bonus in setDocument.Bonuses)
            {
                var pattern = Enum.Parse<ReferenceEnhancementSetBonusRequiresPattern>(
                    bonus.RequiresPattern!,
                    ignoreCase: true);
                switch (pattern)
                {
                    case ReferenceEnhancementSetBonusRequiresPattern.None:
                        stats.NoneRequiresTiers++;
                        break;
                    case ReferenceEnhancementSetBonusRequiresPattern.PieceGate:
                        stats.PieceGateRequiresTiers++;
                        break;
                    case ReferenceEnhancementSetBonusRequiresPattern.PvPMap:
                        stats.PvPMapRequiresTiers++;
                        break;
                    case ReferenceEnhancementSetBonusRequiresPattern.Other:
                        stats.OtherRequiresTiers++;
                        break;
                }
            }
            stats.UnresolvedBonusHelp += setDocument.Bonuses
                .SelectMany(value => value.AutoPowers ?? [])
                .Count(value => string.IsNullOrWhiteSpace(value.DisplayHelp));
            if (setDocument.Bonuses.Count > 0)
            {
                stats.SetsWithBonusTiers++;
            }

            if (!string.IsNullOrWhiteSpace(setDocument.CategoryDisplayText))
            {
                stats.SetsWithCategoryLabels++;
            }

            if (setDocument.MinimumLevel is not null && setDocument.MaximumLevel is not null)
            {
                stats.SetsWithLevelRange++;
            }

            if (string.Equals(categoryCode, "ECToHitDeBuff", StringComparison.Ordinal))
            {
                stats.ToHitDebuffSets++;
            }
        }

        stats.SetsPromoted = candidate.EnhancementSets.Count;
    }

    private static void PromoteEnhancements(
        HomecomingEnhancementCandidateDocument candidate,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> discoveryBoosts,
        HomecomingMessageStore messages,
        Dictionary<string, ItemReferenceRecordDocument> itemDocumentsById,
        Dictionary<string, EnhancementSetReferenceRecordDocument> setDocumentsById,
        PromotionStats stats)
    {
        foreach (var enhancementCandidate in candidate.Enhancements.OrderBy(
                     value => value.AppOwnedId, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(enhancementCandidate.AppOwnedId))
            {
                throw new HomecomingEnhancementPromotionException(
                    $"Logical Enhancement '{enhancementCandidate.DisplayName}' has no app-owned identity.");
            }

            var sourceVariants = enhancementCandidate.SourceVariants
                .OrderBy(value => value.HomecomingSourceId, StringComparer.Ordinal)
                .Select(variant =>
                {
                    if (!discoveryBoosts.TryGetValue(variant.HomecomingSourceId, out var discovery))
                    {
                        throw new HomecomingEnhancementPromotionException(
                            $"Boost '{variant.HomecomingSourceId}' was missing from discovery parse.");
                    }

                    return new VariantPair(variant, discovery);
                })
                .ToArray();

            if (!itemDocumentsById.TryGetValue(enhancementCandidate.AppOwnedId, out var itemDocument))
            {
                itemDocument = new ItemReferenceRecordDocument
                {
                    CatalogItemId = enhancementCandidate.AppOwnedId,
                    Family = nameof(ReferenceItemFamily.Enhancement),
                    Subtype = enhancementCandidate.EnhancementSetAppOwnedId is null
                        ? nameof(ReferenceEnhancementFamily.CraftedInvention)
                        : "SetIO",
                    CurrentDisplayName = enhancementCandidate.DisplayName,
                    ActiveStatus = nameof(ReferenceActiveStatus.Active),
                    VerificationStatus = nameof(ReferenceVerificationStatus.VerifiedMultiSource),
                    Variant = "Regular"
                };
                itemDocumentsById[enhancementCandidate.AppOwnedId] = itemDocument;
                stats.EnhancementsAdded++;
            }
            else
            {
                stats.EnhancementsUpdated++;
            }

            itemDocument.Family = nameof(ReferenceItemFamily.Enhancement);
            itemDocument.CurrentDisplayName = enhancementCandidate.DisplayName;
            itemDocument.EnhancementSetId = enhancementCandidate.EnhancementSetAppOwnedId;
            if (itemDocument.EnhancementSetId is not null
                && !setDocumentsById.ContainsKey(itemDocument.EnhancementSetId))
            {
                throw new HomecomingEnhancementPromotionException(
                    $"Enhancement '{itemDocument.CatalogItemId}' references unknown set '{itemDocument.EnhancementSetId}'.");
            }

            itemDocument.SourceVariants = sourceVariants
                .Select(value =>
                {
                    var displayHelp = HomecomingEnhancementVariantHelpSupport.ResolveVariantDisplayHelp(
                        messages,
                        value.Discovery,
                        itemDocument.CatalogItemId!);
                    var shortHelp = HomecomingEnhancementVariantHelpSupport.ResolveVariantShortHelp(
                        messages,
                        value.Discovery);
                    return new EnhancementSourceVariantReferenceRecordDocument
                    {
                        HomecomingSourceId = value.Variant.HomecomingSourceId,
                        SourceForm = value.Variant.SourceForm,
                        Icon = string.IsNullOrWhiteSpace(value.Discovery.Icon) ? null : value.Discovery.Icon,
                        DisplayHelp = displayHelp,
                        ShortHelp = shortHelp
                    };
                })
                .ToList();

            var resolvedDisplayHelps = itemDocument.SourceVariants
                .Select(value => value.DisplayHelp)
                .ToArray();
            itemDocument.DisplayHelp = HomecomingEnhancementVariantHelpSupport.ResolveLogicalHelpFromVariantTexts(
                resolvedDisplayHelps);

            var resolvedShortHelps = itemDocument.SourceVariants
                .Select(value => value.ShortHelp)
                .ToArray();
            itemDocument.ShortHelp = HomecomingEnhancementVariantHelpSupport.ResolveLogicalHelpFromVariantTexts(
                resolvedShortHelps);

            if (HomecomingEnhancementVariantHelpSupport.HasHelpDisagreement(resolvedDisplayHelps))
            {
                stats.DisplayHelpDisagreementGroups++;
            }
            else if (resolvedDisplayHelps.Any(value => value is not null))
            {
                stats.DisplayHelpAgreementGroups++;
            }

            if (HomecomingEnhancementVariantHelpSupport.HasHelpDisagreement(resolvedShortHelps))
            {
                stats.ShortHelpDisagreementGroups++;
            }
            else if (resolvedShortHelps.Any(value => value is not null))
            {
                stats.ShortHelpAgreementGroups++;
            }

            foreach (var pair in sourceVariants.Zip(itemDocument.SourceVariants))
            {
                if (!string.IsNullOrWhiteSpace(pair.Second.DisplayHelp))
                {
                    stats.VariantsWithDisplayHelp++;
                }
                else if (string.IsNullOrWhiteSpace(pair.First.Discovery.DisplayHelpMessageKey))
                {
                    stats.VariantsWithAbsentOptionalDisplayHelp++;
                }

                if (!string.IsNullOrWhiteSpace(pair.Second.ShortHelp))
                {
                    stats.VariantsWithShortHelp++;
                }
                else if (string.IsNullOrWhiteSpace(pair.First.Discovery.ShortHelpMessageKey))
                {
                    stats.VariantsWithAbsentOptionalShortHelp++;
                }
            }

            stats.TotalSourceVariants += itemDocument.SourceVariants.Count;

            var icons = itemDocument.SourceVariants
                .Select(value => value.Icon)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            itemDocument.Icon = icons.Length == 1 ? icons[0] : null;
            if (icons.Length > 1)
            {
                stats.DifferingVariantIconEnhancements++;
            }

            if (icons.Length > 0)
            {
                stats.EnhancementsWithIcons++;
            }

            if (enhancementCandidate.EnhancementSetAppOwnedId is null)
            {
                var boostTypes = sourceVariants
                    .SelectMany(value => value.Discovery.NonOriginBoostTypes)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                var enhancementFamily = HomecomingEnhancementFamilySupport.ClassifyLogicalEnhancement(
                    enhancementCandidate.DisplayName,
                    sourceVariants
                        .Select(value => (value.Variant.HomecomingSourceId, value.Discovery))
                        .ToArray());
                itemDocument.EnhancementFamily = enhancementFamily.ToString();
                itemDocument.Subtype = enhancementFamily.ToString();
                itemDocument.CommonIoBoostType =
                    HomecomingEnhancementFamilySupport.ResolveApplicabilityKey(boostTypes);
                itemDocument.CommonIoBoostTypeDisplayText =
                    HomecomingEnhancementFamilySupport.ResolveApplicabilityDisplayText(
                        enhancementFamily,
                        boostTypes);

                stats.NonSetEnhancements++;
                stats.EnhancementFamilyCounts[enhancementFamily] =
                    stats.EnhancementFamilyCounts.GetValueOrDefault(enhancementFamily) + 1;

                if (enhancementFamily == ReferenceEnhancementFamily.CraftedInvention
                    && itemDocument.CommonIoBoostType is not null)
                {
                    stats.CraftedInventionStructuralKeys.Add(itemDocument.CommonIoBoostType);
                    if (itemDocument.CommonIoBoostTypeDisplayText is null)
                    {
                        stats.UnlabeledCraftedInventionKeys.Add(itemDocument.CommonIoBoostType);
                    }
                }
            }
            else
            {
                itemDocument.EnhancementFamily = null;
                itemDocument.CommonIoBoostType = null;
                itemDocument.CommonIoBoostTypeDisplayText = null;
                itemDocument.Subtype = "SetIO";
            }
        }

        stats.EnhancementsPromoted = candidate.Enhancements.Count;
        stats.CraftedInventionLabelCoverage = stats.CraftedInventionStructuralKeys.Count(key =>
            !stats.UnlabeledCraftedInventionKeys.Contains(key)
            && !key.Contains('+', StringComparison.Ordinal));
    }

    private static void PromoteResolverFacts(
        HomecomingEnhancementSourceSnapshot sourceSnapshot,
        HomecomingEnhancementCandidateDocument candidate,
        ItemReferenceCatalogDocument document,
        Dictionary<string, ItemReferenceRecordDocument> itemDocumentsById,
        Dictionary<string, EnhancementSetReferenceRecordDocument> setDocumentsById,
        PromotionStats stats)
    {
        var liveEnhIds = candidate.Enhancements
            .Where(value => !string.IsNullOrWhiteSpace(value.AppOwnedId))
            .Select(value => value.AppOwnedId!)
            .ToHashSet(StringComparer.Ordinal);
        var liveSetIds = candidate.EnhancementSets
            .Where(value => !string.IsNullOrWhiteSpace(value.AppOwnedId))
            .Select(value => value.AppOwnedId!)
            .ToHashSet(StringComparer.Ordinal);
        var referencedTables = new HashSet<string>(StringComparer.Ordinal);

        foreach (var itemDocument in itemDocumentsById.Values.OrderBy(
                     value => value.CatalogItemId, StringComparer.Ordinal))
        {
            if (!string.Equals(itemDocument.Family, nameof(ReferenceItemFamily.Enhancement), StringComparison.Ordinal)
                || itemDocument.SourceVariants is null)
            {
                continue;
            }

            var isCurrent = !string.IsNullOrWhiteSpace(itemDocument.CatalogItemId)
                && liveEnhIds.Contains(itemDocument.CatalogItemId);
            var promotedVariants = new List<EnhancementSourceVariantReferenceRecordDocument>(
                itemDocument.SourceVariants.Count);
            foreach (var variant in itemDocument.SourceVariants.OrderBy(
                         value => value.HomecomingSourceId, StringComparer.Ordinal))
            {
                stats.CurrentSourceVariants++;
                if (!isCurrent)
                {
                    promotedVariants.Add(
                        HomecomingEnhancementResolverPromotionSupport.CreateEmptyResolverVariantDocument(variant));
                    continue;
                }

                var facts = ReadResolverFacts(sourceSnapshot, variant.HomecomingSourceId!);
                promotedVariants.Add(
                    HomecomingEnhancementResolverPromotionSupport.CreateVariantDocument(variant, facts));
                stats.VariantsWithResolverFacts++;
                foreach (var effect in facts.Effects)
                {
                    referencedTables.Add(effect.Table);
                }

                if (!string.IsNullOrWhiteSpace(variant.DisplayHelp)
                    && HomecomingEnhancementResolverPromotionSupport
                        .ExtractScaleTokens(variant.DisplayHelp)
                        .Any())
                {
                    stats.TokenBearingVariants++;
                    stats.TokenBearingVariantsCovered++;
                }
            }

            itemDocument.SourceVariants = promotedVariants;
        }

        foreach (var setDocument in setDocumentsById.Values.OrderBy(
                     value => value.CatalogItemId, StringComparer.Ordinal))
        {
            if (setDocument.Bonuses is null
                || string.IsNullOrWhiteSpace(setDocument.CatalogItemId)
                || !liveSetIds.Contains(setDocument.CatalogItemId))
            {
                continue;
            }

            var promotedBonuses = new List<EnhancementSetBonusReferenceRecordDocument>(setDocument.Bonuses.Count);
            foreach (var bonus in setDocument.Bonuses)
            {
                var promotedPowers = new List<EnhancementSetBonusPowerReferenceRecordDocument>();
                foreach (var autoPower in bonus.AutoPowers ?? [])
                {
                    if (string.IsNullOrWhiteSpace(autoPower.HomecomingSourceId))
                    {
                        throw new HomecomingEnhancementPromotionException(
                            $"Set '{setDocument.CatalogItemId}' has a bonus auto-power without HomecomingSourceId.");
                    }

                    if (!autoPower.HomecomingSourceId.StartsWith("Set_Bonus.", StringComparison.Ordinal))
                    {
                        promotedPowers.Add(autoPower);
                        continue;
                    }

                    var facts = ReadResolverFacts(sourceSnapshot, autoPower.HomecomingSourceId);
                    promotedPowers.Add(
                        HomecomingEnhancementResolverPromotionSupport.CreateBonusPowerDocument(autoPower, facts));
                    stats.SetBonusPowersWithResolverFacts++;
                    foreach (var effect in facts.Effects)
                    {
                        referencedTables.Add(effect.Table);
                    }
                }

                promotedBonuses.Add(new EnhancementSetBonusReferenceRecordDocument
                {
                    MinimumBoosts = bonus.MinimumBoosts,
                    MaximumBoosts = bonus.MaximumBoosts,
                    RequiresPattern = bonus.RequiresPattern,
                    RequiresTokens = bonus.RequiresTokens,
                    RequiredEnhancementIds = bonus.RequiredEnhancementIds,
                    AutoPowers = promotedPowers
                });
            }

            setDocument.Bonuses = promotedBonuses;
        }

        stats.ReferencedNamedTables = referencedTables.Count;
        var promotedTables = sourceSnapshot.PromoteNamedTables(referencedTables);
        stats.PromotedNamedTables = promotedTables.Count;
        document.EnhancementResolverNamedTables = promotedTables
            .Select(pair => new EnhancementResolverNamedTableReferenceRecordDocument
            {
                Name = pair.Key,
                Values = pair.Value.ToList()
            })
            .OrderBy(value => value.Name, StringComparer.Ordinal)
            .ToList();

        HomecomingEnhancementResolverPromotionSupport.ValidateTokenCoverage(document, liveEnhIds);
    }

    private static HomecomingBoostResolverFactsDiscovery ReadResolverFacts(
        HomecomingEnhancementSourceSnapshot sourceSnapshot,
        string sourceId)
    {
        try
        {
            if (sourceSnapshot.ResolverFactsBySourceId.TryGetValue(sourceId, out var facts))
            {
                return facts;
            }

            throw new HomecomingPowersBoostDiscoveryException($"Boost '{sourceId}' record was not located.");
        }
        catch (HomecomingPowersBoostDiscoveryException exception)
        {
            throw new HomecomingEnhancementPromotionException(
                $"Resolver fact promotion failed for '{sourceId}': {exception.Message}",
                exception);
        }
    }

    private static string ResolveRequiredBonusAutoPowerHelp(
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> powerDiscoveryBySourceId,
        HomecomingMessageStore messages,
        string homecomingSetId,
        string autoPowerId)
    {
        if (!powerDiscoveryBySourceId.TryGetValue(autoPowerId, out var discovery)
            || string.IsNullOrWhiteSpace(discovery.DisplayHelpMessageKey))
        {
            throw new HomecomingEnhancementPromotionException(
                $"Boost Set '{homecomingSetId}' auto-power '{autoPowerId}' has no display_help message key in powers.bin.");
        }

        return ResolveRequiredHelp(
            messages,
            discovery.DisplayHelpMessageKey,
            autoPowerId);
    }

    private static void ApplyHomecomingServerAvailability(
        Dictionary<string, ItemReferenceRecordDocument> itemDocumentsById,
        Dictionary<string, EnhancementSetReferenceRecordDocument> setDocumentsById,
        HomecomingEnhancementCandidateDocument candidate,
        PromotionStats stats)
    {
        var liveEnhIds = candidate.Enhancements
            .Where(value => !string.IsNullOrWhiteSpace(value.AppOwnedId))
            .Select(value => value.AppOwnedId!)
            .ToHashSet(StringComparer.Ordinal);
        var liveSetIds = candidate.EnhancementSets
            .Where(value => !string.IsNullOrWhiteSpace(value.AppOwnedId))
            .Select(value => value.AppOwnedId!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var setDocument in setDocumentsById.Values)
        {
            if (string.IsNullOrWhiteSpace(setDocument.CatalogItemId))
            {
                throw new HomecomingEnhancementPromotionException(
                    "Enhancement Set promotion produced a record without CatalogItemId.");
            }

            var status = liveSetIds.Contains(setDocument.CatalogItemId)
                ? ReferenceServerAvailabilityStatus.Current
                : ReferenceServerAvailabilityStatus.Historical;
            setDocument.ServerAvailability = CreateServerAvailabilityDocument(status);
            if (status == ReferenceServerAvailabilityStatus.Current)
            {
                stats.HomecomingCurrentSets++;
            }
            else
            {
                stats.HomecomingHistoricalSets++;
            }
        }

        var historicalSetIds = setDocumentsById.Values
            .Where(value => !liveSetIds.Contains(value.CatalogItemId!))
            .Select(value => value.CatalogItemId!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var itemDocument in itemDocumentsById.Values)
        {
            if (!string.Equals(
                    itemDocument.Family,
                    nameof(ReferenceItemFamily.Enhancement),
                    StringComparison.Ordinal))
            {
                itemDocument.ServerAvailability = null;
                continue;
            }

            if (string.IsNullOrWhiteSpace(itemDocument.CatalogItemId))
            {
                throw new HomecomingEnhancementPromotionException(
                    "Enhancement promotion produced a record without CatalogItemId.");
            }

            var status = liveEnhIds.Contains(itemDocument.CatalogItemId)
                ? ReferenceServerAvailabilityStatus.Current
                : ReferenceServerAvailabilityStatus.Historical;
            itemDocument.ServerAvailability = CreateServerAvailabilityDocument(status);
            if (status == ReferenceServerAvailabilityStatus.Current)
            {
                stats.HomecomingCurrentEnhancements++;
                continue;
            }

            stats.HomecomingHistoricalEnhancements++;
            var bucket = ClassifyHistoricalEnhancement(
                itemDocument,
                historicalSetIds,
                liveSetIds);
            stats.HistoricalEnhancementBuckets[bucket] =
                stats.HistoricalEnhancementBuckets.GetValueOrDefault(bucket) + 1;
        }

        ValidateAvailabilityCensus(stats);
    }

    private static string ClassifyHistoricalEnhancement(
        ItemReferenceRecordDocument item,
        IReadOnlySet<string> historicalSetIds,
        IReadOnlySet<string> liveSetIds)
    {
        if (!string.IsNullOrWhiteSpace(item.EnhancementSetId)
            && historicalSetIds.Contains(item.EnhancementSetId))
        {
            return "PieceOfHistoricalSet";
        }

        if (!string.IsNullOrWhiteSpace(item.EnhancementSetId)
            && liveSetIds.Contains(item.EnhancementSetId))
        {
            return "SetPieceMissingFromLiveLogical";
        }

        if (item.CurrentDisplayName?.StartsWith("Invention:", StringComparison.Ordinal) == true)
        {
            return "LegacyNonSetInventionNamed";
        }

        if (item.CurrentDisplayName?.Contains(':', StringComparison.Ordinal) == true)
        {
            return "LegacyNonSetOther";
        }

        return "UnclassifiedHistoricalEnhancement";
    }

    private static void ReconcileEnhancementAliases(
        ItemReferenceCatalogDocument document,
        IReadOnlyDictionary<string, ItemReferenceRecordDocument> itemDocumentsById)
    {
        var currentEnhancements = itemDocumentsById.Values
            .Where(item =>
                string.Equals(item.Family, nameof(ReferenceItemFamily.Enhancement), StringComparison.Ordinal)
                && IsCurrentAvailability(item.ServerAvailability))
            .ToArray();

        var currentOwnerByLookupKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in currentEnhancements)
        {
            AddCurrentAliasOwner(currentOwnerByLookupKey, item.CurrentDisplayName, item.CatalogItemId!);
        }

        var aliases = document.Aliases ?? [];
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias.CatalogItemId)
                || string.IsNullOrWhiteSpace(alias.Text)
                || !itemDocumentsById.TryGetValue(alias.CatalogItemId, out var item)
                || !string.Equals(item.Family, nameof(ReferenceItemFamily.Enhancement), StringComparison.Ordinal))
            {
                continue;
            }

            var lookupKey = ItemReferenceLookup.NormalizeLookupKey(alias.Text);
            if (string.IsNullOrEmpty(lookupKey))
            {
                continue;
            }

            if (currentOwnerByLookupKey.TryGetValue(lookupKey, out var currentOwnerId)
                && !string.Equals(currentOwnerId, alias.CatalogItemId, StringComparison.Ordinal)
                && IsHistoricalAvailability(item.ServerAvailability))
            {
                alias.CatalogItemId = currentOwnerId;
            }
        }

        var existingAliasKeys = aliases
            .Select(alias => (
                CatalogItemId: alias.CatalogItemId ?? string.Empty,
                LookupKey: ItemReferenceLookup.NormalizeLookupKey(alias.Text ?? string.Empty)))
            .Where(pair => !string.IsNullOrEmpty(pair.LookupKey))
            .ToHashSet();

        foreach (var item in currentEnhancements)
        {
            var lookupKey = ItemReferenceLookup.NormalizeLookupKey(item.CurrentDisplayName ?? string.Empty);
            if (string.IsNullOrEmpty(lookupKey))
            {
                continue;
            }

            var receiptKey = (item.CatalogItemId!, lookupKey);
            if (existingAliasKeys.Contains(receiptKey))
            {
                continue;
            }

            aliases.Add(new ItemReferenceAliasRecordDocument
            {
                CatalogItemId = item.CatalogItemId,
                Locale = "en",
                Text = item.CurrentDisplayName,
                NameKind = nameof(ReferenceAliasNameKind.LogReceipt),
                IsPreferred = true
            });
            existingAliasKeys.Add(receiptKey);
        }

        document.Aliases = aliases
            .OrderBy(value => value.CatalogItemId, StringComparer.Ordinal)
            .ThenBy(value => value.Text, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddCurrentAliasOwner(
        IDictionary<string, string> currentOwnerByLookupKey,
        string? text,
        string catalogItemId)
    {
        var lookupKey = ItemReferenceLookup.NormalizeLookupKey(text ?? string.Empty);
        if (string.IsNullOrEmpty(lookupKey))
        {
            return;
        }

        currentOwnerByLookupKey[lookupKey] = catalogItemId;
    }

    private static bool IsCurrentAvailability(List<ReferenceServerAvailabilityDocument>? availability) =>
        ParseServerAvailability(availability) is [var entry]
        && entry.Status == ReferenceServerAvailabilityStatus.Current;

    private static bool IsHistoricalAvailability(List<ReferenceServerAvailabilityDocument>? availability) =>
        ParseServerAvailability(availability) is [var entry]
        && entry.Status == ReferenceServerAvailabilityStatus.Historical;

    private static void ValidateAvailabilityCensus(PromotionStats stats)
    {
        if (stats.HomecomingHistoricalEnhancements != 169)
        {
            throw new HomecomingEnhancementPromotionException(
                $"Expected 169 historical Homecoming Enhancements; found {stats.HomecomingHistoricalEnhancements}.");
        }

        if (stats.HomecomingCurrentEnhancements != 1628)
        {
            throw new HomecomingEnhancementPromotionException(
                $"Expected 1628 current Homecoming Enhancements; found {stats.HomecomingCurrentEnhancements}.");
        }

        if (stats.HomecomingHistoricalSets != 3)
        {
            throw new HomecomingEnhancementPromotionException(
                $"Expected 3 historical Homecoming Sets; found {stats.HomecomingHistoricalSets}.");
        }

        if (stats.HomecomingCurrentSets != 227)
        {
            throw new HomecomingEnhancementPromotionException(
                $"Expected 227 current Homecoming Sets; found {stats.HomecomingCurrentSets}.");
        }

        if (stats.HistoricalEnhancementBuckets.GetValueOrDefault("UnclassifiedHistoricalEnhancement") > 0)
        {
            throw new HomecomingEnhancementPromotionException(
                "Historical Enhancement classification left unexplained records.");
        }
    }

    private static List<ReferenceServerAvailabilityDocument> CreateServerAvailabilityDocument(
        ReferenceServerAvailabilityStatus status) =>
        [
            new ReferenceServerAvailabilityDocument
            {
                ServerKey = ReferenceServerKey.Homecoming,
                Status = status.ToString()
            }
        ];

    private static IReadOnlyList<ReferenceServerAvailability> ParseServerAvailability(
        List<ReferenceServerAvailabilityDocument>? documents)
    {
        if (documents is null || documents.Count == 0)
        {
            return Array.Empty<ReferenceServerAvailability>();
        }

        return documents
            .Select(document => new ReferenceServerAvailability
            {
                ServerKey = document.ServerKey ?? ReferenceServerKey.Homecoming,
                Status = Enum.Parse<ReferenceServerAvailabilityStatus>(document.Status!, ignoreCase: true)
            })
            .ToArray();
    }

    private static string ResolveRequiredHelp(
        HomecomingMessageStore messages,
        string messageKey,
        string catalogItemId)
    {
        if (!messages.TryResolve(messageKey, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new HomecomingEnhancementPromotionException(
                $"Enhancement '{catalogItemId}' display_help message '{messageKey}' is unresolved.");
        }

        return value;
    }

    private static string? ResolveOptionalMessage(HomecomingMessageStore messages, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)
            || !messages.TryResolve(key, out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value;
    }

    private static void WriteSummary(TextWriter output, HomecomingEnhancementPromotionResult result)
    {
        var stats = result.Stats;
        output.WriteLine("Enhancement promotion: PASS");
        output.WriteLine($"Catalog: {result.CatalogPath}");
        output.WriteLine($"Build: {result.BuildVersion}");
        output.WriteLine($"Enhancement Sets promoted: {stats.SetsPromoted}");
        output.WriteLine($"Sets updated: {stats.SetsUpdated}");
        output.WriteLine($"Sets added: {stats.SetsAdded}");
        output.WriteLine($"Logical Enhancements promoted: {stats.EnhancementsPromoted}");
        output.WriteLine($"Enhancements updated: {stats.EnhancementsUpdated}");
        output.WriteLine($"Enhancements added: {stats.EnhancementsAdded}");
        output.WriteLine($"Non-set logical Enhancements: {stats.NonSetEnhancements}");
        foreach (var family in Enum.GetValues<ReferenceEnhancementFamily>().OrderBy(value => value.ToString(), StringComparer.Ordinal))
        {
            stats.EnhancementFamilyCounts.TryGetValue(family, out var count);
            output.WriteLine($"{family}: {count}");
        }

        output.WriteLine(
            $"Distinct CraftedInvention applicability keys: {stats.CraftedInventionStructuralKeys.Count} ({string.Join(", ", stats.CraftedInventionStructuralKeys.Order(StringComparer.Ordinal))})");
        output.WriteLine(
            $"CraftedInvention friendly-label coverage: {stats.CraftedInventionLabelCoverage}/{stats.CraftedInventionStructuralKeys.Count}");
        if (stats.UnlabeledCraftedInventionKeys.Count > 0)
        {
            output.WriteLine(
                $"Unlabeled CraftedInvention applicability keys: {string.Join(", ", stats.UnlabeledCraftedInventionKeys.Order(StringComparer.Ordinal))}");
        }

        output.WriteLine($"Enhancements with icon identifiers: {stats.EnhancementsWithIcons}");
        output.WriteLine(
            $"Logical Enhancements with differing variant icons: {stats.DifferingVariantIconEnhancements}");
        output.WriteLine($"Sets with bonus tiers: {stats.SetsWithBonusTiers}");
        output.WriteLine($"Bonus tiers: {stats.BonusTiers}");
        output.WriteLine(
            $"Requires-bearing tiers: {stats.PieceGateRequiresTiers + stats.PvPMapRequiresTiers + stats.OtherRequiresTiers}");
        output.WriteLine($"None Requires: {stats.NoneRequiresTiers}");
        output.WriteLine($"PieceGate Requires: {stats.PieceGateRequiresTiers}");
        output.WriteLine($"PvPMap Requires: {stats.PvPMapRequiresTiers}");
        output.WriteLine($"Other Requires: {stats.OtherRequiresTiers}");
        output.WriteLine($"Bonus auto-power relationships: {stats.BonusAutoPowers}");
        output.WriteLine($"Unresolved bonus help: {stats.UnresolvedBonusHelp}");
        output.WriteLine($"Sets with category labels: {stats.SetsWithCategoryLabels}");
        output.WriteLine($"Sets with level range: {stats.SetsWithLevelRange}");
        output.WriteLine($"ECToHitDeBuff sets: {stats.ToHitDebuffSets}");
        output.WriteLine($"Homecoming Current Enhancements: {stats.HomecomingCurrentEnhancements}");
        output.WriteLine($"Homecoming Historical Enhancements: {stats.HomecomingHistoricalEnhancements}");
        output.WriteLine($"Homecoming Current Sets: {stats.HomecomingCurrentSets}");
        output.WriteLine($"Homecoming Historical Sets: {stats.HomecomingHistoricalSets}");
        output.WriteLine($"Total source variants: {stats.TotalSourceVariants}");
        output.WriteLine($"Variants with DisplayHelp: {stats.VariantsWithDisplayHelp}");
        output.WriteLine($"Variants with ShortHelp: {stats.VariantsWithShortHelp}");
        output.WriteLine(
            $"Variants with absent optional DisplayHelp: {stats.VariantsWithAbsentOptionalDisplayHelp}");
        output.WriteLine(
            $"Variants with absent optional ShortHelp: {stats.VariantsWithAbsentOptionalShortHelp}");
        output.WriteLine($"Logical DisplayHelp agreement groups: {stats.DisplayHelpAgreementGroups}");
        output.WriteLine($"Logical DisplayHelp disagreement groups: {stats.DisplayHelpDisagreementGroups}");
        output.WriteLine($"Logical ShortHelp agreement groups: {stats.ShortHelpAgreementGroups}");
        output.WriteLine($"Logical ShortHelp disagreement groups: {stats.ShortHelpDisagreementGroups}");
        output.WriteLine($"Current source variants: {stats.CurrentSourceVariants}");
        output.WriteLine($"Variants with resolver facts: {stats.VariantsWithResolverFacts}");
        output.WriteLine($"Token-bearing variants: {stats.TokenBearingVariants}");
        output.WriteLine($"Token-bearing variants covered: {stats.TokenBearingVariantsCovered}");
        output.WriteLine($"Referenced NamedTables: {stats.ReferencedNamedTables}");
        output.WriteLine($"Promoted NamedTables: {stats.PromotedNamedTables}");
        output.WriteLine($"Set bonus powers with resolver facts: {stats.SetBonusPowersWithResolverFacts}");
        foreach (var bucket in stats.HistoricalEnhancementBuckets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"{bucket.Key}: {bucket.Value}");
        }

        output.WriteLine($"Catalog SHA256: {result.CatalogSha256}");
        output.WriteLine($"Determinism SHA256: {result.DeterminismSha256}");
    }

    private sealed record VariantPair(
        HomecomingEnhancementSourceVariant Variant,
        HomecomingBoostDiscoveryRecord Discovery);

    internal sealed class PromotionStats
    {
        internal int SetsPromoted;
        internal int SetsUpdated;
        internal int SetsAdded;
        internal int EnhancementsPromoted;
        internal int EnhancementsUpdated;
        internal int EnhancementsAdded;
        internal int NonSetEnhancements;
        internal int CraftedInventionLabelCoverage;
        internal int EnhancementsWithIcons;
        internal int DifferingVariantIconEnhancements;
        internal int SetsWithBonusTiers;
        internal int BonusTiers;
        internal int BonusAutoPowers;
        internal int NoneRequiresTiers;
        internal int PieceGateRequiresTiers;
        internal int PvPMapRequiresTiers;
        internal int OtherRequiresTiers;
        internal int UnresolvedBonusHelp;
        internal int SetsWithCategoryLabels;
        internal int SetsWithLevelRange;
        internal int ToHitDebuffSets;
        internal int HomecomingCurrentEnhancements;
        internal int HomecomingHistoricalEnhancements;
        internal int HomecomingCurrentSets;
        internal int HomecomingHistoricalSets;
        internal Dictionary<string, int> HistoricalEnhancementBuckets { get; } =
            new(StringComparer.Ordinal);
        internal Dictionary<ReferenceEnhancementFamily, int> EnhancementFamilyCounts { get; } = [];
        internal HashSet<string> CraftedInventionStructuralKeys { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> UnlabeledCraftedInventionKeys { get; } = new(StringComparer.Ordinal);
        internal int TotalSourceVariants;
        internal int VariantsWithDisplayHelp;
        internal int VariantsWithShortHelp;
        internal int VariantsWithAbsentOptionalDisplayHelp;
        internal int VariantsWithAbsentOptionalShortHelp;
        internal int DisplayHelpAgreementGroups;
        internal int DisplayHelpDisagreementGroups;
        internal int ShortHelpAgreementGroups;
        internal int ShortHelpDisagreementGroups;
        internal int CurrentSourceVariants;
        internal int VariantsWithResolverFacts;
        internal int TokenBearingVariants;
        internal int TokenBearingVariantsCovered;
        internal int ReferencedNamedTables;
        internal int PromotedNamedTables;
        internal int SetBonusPowersWithResolverFacts;
    }
}

internal sealed record HomecomingEnhancementPromotionPassResult(
    HomecomingEnhancementPromotionResult Result,
    byte[] SerializedCatalog);

internal sealed record HomecomingEnhancementPromotionResult(
    string CatalogPath,
    string BuildVersion,
    string CatalogSha256,
    string DeterminismSha256,
    HomecomingEnhancementPromotionCommand.PromotionStats Stats);

internal sealed class HomecomingEnhancementPromotionException : Exception
{
    internal HomecomingEnhancementPromotionException(string message)
        : base(message)
    {
    }

    internal HomecomingEnhancementPromotionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

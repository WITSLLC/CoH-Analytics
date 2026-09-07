using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

/// <summary>
/// Read-only forensic census for Enhancement Reference repair design.
/// Does not modify production catalogs or Homecoming files.
/// </summary>
internal static class HomecomingEnhancementRepairDiscoveryCommand
{
    private const string MessageMemberName = "bin/clientmessages-en.bin";
    private static readonly string[] OriginBoostTypes =
    [
        "Science", "Mutation", "Magic", "Technology", "Natural"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (!TryParseArgs(args, out var installRoot, out var reportPath, out var failureReason))
        {
            error.WriteLine(failureReason);
            error.WriteLine(
                "Usage: discover-enhancement-repair-facts --install <HomecomingRoot> --out <report.json>");
            return 1;
        }

        try
        {
            var before = SnapshotHashes(installRoot);
            var report = Discover(installRoot);
            var after = SnapshotHashes(installRoot);
            if (!before.SequenceEqual(after, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("Homecoming installation changed during discovery.");
            }

            report.HomecomingHashesUnchanged = true;
            report.BinPiggSha256 = before[0].Split('|')[1];
            report.BinPowersPiggSha256 = before[1].Split('|')[1];

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report, JsonOptions) + "\n");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            File.WriteAllBytes(reportPath, bytes);
            WriteSummary(output, report, reportPath);
            return 0;
        }
        catch (Exception exception)
        {
            error.WriteLine($"Enhancement repair discovery: FAIL — {exception.Message}");
            return 1;
        }
    }

    internal static EnhancementRepairDiscoveryReport Discover(string installRoot)
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(installRoot);
        var boostSetBytes = HomecomingPiggMemberReader.ReadMember(
            source.BinPiggPath,
            HomecomingEnhancementCandidateGenerator.BoostSetsMember);
        var powersBytes = HomecomingPiggMemberReader.ReadMember(
            source.BinPowersPiggPath,
            HomecomingEnhancementCandidateGenerator.PowersMember);
        var messages = HomecomingMessageStoreReader.Read(
            HomecomingPiggMemberReader.ReadMember(source.BinPiggPath, MessageMemberName));

        var productionSets = HomecomingBoostSetsReader.Read(boostSetBytes);
        var requiresSets = HomecomingBoostSetBonusRequiresDiscoveryReader.Read(boostSetBytes);
        var legacyDiscoverySets = HomecomingBoostSetsDiscoveryReader.Read(boostSetBytes);
        var concreteBoosts = HomecomingPowersReader.ReadBoosts(powersBytes);
        var discoveryBoosts = HomecomingPowersBoostDiscoveryReader.ReadBoosts(powersBytes)
            .Where(value => value.SourceId.StartsWith("Boosts.", StringComparison.Ordinal))
            .ToDictionary(value => value.SourceId, StringComparer.Ordinal);

        var currentCatalog = HomecomingEnhancementCandidateGenerator.LoadEmbeddedCurrentCatalog();
        var candidate = HomecomingEnhancementCandidateGenerator.Create(
            concreteBoosts,
            productionSets,
            messages,
            discoveryBoosts,
            currentCatalog.Enhancements,
            currentCatalog.EnhancementSets,
            source.BuildVersion,
            source.PackageRevision);

        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        var bonusFacts = BuildBonusFacts(requiresSets, legacyDiscoverySets, messages);
        var nonSetFacts = BuildNonSetFacts(candidate, discoveryBoosts, messages);
        var familyFacts = BuildFamilyFacts(candidate, discoveryBoosts);
        var historicalFacts = BuildHistoricalFacts(candidate, productionSets, catalog, messages);
        var helpFacts = BuildHelpFacts(candidate, discoveryBoosts, messages);

        return new EnhancementRepairDiscoveryReport
        {
            BuildVersion = source.BuildVersion,
            PackageRevision = source.PackageRevision,
            LiveSetCount = productionSets.Count,
            LiveConcreteBoostCount = concreteBoosts.Count,
            LiveLogicalEnhancementCount = candidate.Enhancements.Count,
            Bonus = bonusFacts,
            NonSetIdentity = nonSetFacts,
            Families = familyFacts,
            Historical = historicalFacts,
            VariantHelp = helpFacts
        };
    }

    private static BonusRequiresDiscoveryFacts BuildBonusFacts(
        IReadOnlyList<HomecomingBoostSetBonusRequiresDiscoveryRecord> requiresSets,
        IReadOnlyList<HomecomingBoostSetDiscoveryRecord> legacyDiscoverySets,
        HomecomingMessageStore messages)
    {
        var allTiers = requiresSets.SelectMany(set => set.Bonuses.Select(bonus => (set, bonus))).ToArray();
        var nonempty = allTiers.Where(value => value.bonus.RequiresTokens.Count > 0).ToArray();
        var tokenFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        var lengthDistribution = new Dictionary<int, int>();
        var patternCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var uniqueOffsets = new HashSet<uint>();
        var setsWithRequires = new HashSet<string>(StringComparer.Ordinal);
        var unusual = new List<string>();
        var examples = new List<BonusRequiresExample>();

        foreach (var (set, bonus) in allTiers)
        {
            lengthDistribution[bonus.RequiresTokens.Count] =
                lengthDistribution.GetValueOrDefault(bonus.RequiresTokens.Count) + 1;
            if (bonus.LeadingUnknown != 0 || bonus.TrailingUnknown != 0)
            {
                unusual.Add(
                    $"{set.HomecomingSetId}[{bonus.BonusIndex}] leading={bonus.LeadingUnknown} trailing={bonus.TrailingUnknown}");
            }

            if (bonus.RequiresTokens.Count == 0)
            {
                continue;
            }

            setsWithRequires.Add(set.HomecomingSetId);
            foreach (var offset in bonus.RequiresOffsets)
            {
                uniqueOffsets.Add(offset);
            }

            foreach (var token in bonus.RequiresTokens)
            {
                tokenFrequency[token] = tokenFrequency.GetValueOrDefault(token) + 1;
            }

            var pattern = ClassifyRequiresPattern(bonus.RequiresTokens);
            patternCounts[pattern] = patternCounts.GetValueOrDefault(pattern) + 1;
        }

        foreach (var setId in new[]
                 {
                     "Aegis",
                     "Luck_of_the_Gambler",
                     "Reactive_Defenses",
                     "Preventive_Medicine",
                     "Karma",
                     "Steadfast_Protection",
                     "Command_of_the_Mastermind",
                     "Superior_Command_of_the_Mastermind",
                     "Gladiators_Armor",
                     "Panacea",
                     "Absolute_Amazement"
                 })
        {
            var set = requiresSets.FirstOrDefault(value => value.HomecomingSetId == setId);
            if (set is null)
            {
                continue;
            }

            messages.TryResolve(
                // display names come from production path; keep set id as label
                setId,
                out _);
            examples.Add(new BonusRequiresExample
            {
                HomecomingSetId = setId,
                BonusCount = set.Bonuses.Count,
                NonEmptyRequiresTiers = set.Bonuses
                    .Where(bonus => bonus.RequiresTokens.Count > 0)
                    .Select(bonus => new BonusRequiresTierExample
                    {
                        BonusIndex = bonus.BonusIndex,
                        MinimumBoosts = bonus.MinimumBoosts,
                        MaximumBoosts = bonus.MaximumBoosts,
                        RequiresTokens = bonus.RequiresTokens.ToArray(),
                        AutoPowerSourceIds = bonus.AutoPowerSourceIds.ToArray(),
                        Pattern = ClassifyRequiresPattern(bonus.RequiresTokens)
                    })
                    .ToArray()
            });
        }

        var legacyEmpty = legacyDiscoverySets
            .Where(value => value.Bonuses.Count == 0)
            .Select(value => value.HomecomingSetId)
            .ToHashSet(StringComparer.Ordinal);
        var falselyCleared = setsWithRequires
            .Where(legacyEmpty.Contains)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var emptyWithoutRequires = legacyDiscoverySets
            .Where(value => value.Bonuses.Count == 0 && !setsWithRequires.Contains(value.HomecomingSetId))
            .Select(value => value.HomecomingSetId)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return new BonusRequiresDiscoveryFacts
        {
            TotalSets = requiresSets.Count,
            TotalBonusTiers = allTiers.Length,
            TotalAutoPowerRelationships = allTiers.Sum(value => value.bonus.AutoPowerSourceIds.Count),
            TiersWithNonEmptyRequires = nonempty.Length,
            SetsWithNonEmptyRequires = setsWithRequires.Count,
            TotalRequiresTokens = nonempty.Sum(value => value.bonus.RequiresTokens.Count),
            UniqueRequiresOffsets = uniqueOffsets.Count,
            RequiresLengthDistribution = lengthDistribution
                .OrderBy(pair => pair.Key)
                .ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
            PatternCounts = patternCounts
                .OrderByDescending(pair => pair.Value)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            TopTokens = tokenFrequency
                .OrderByDescending(pair => pair.Value)
                .Take(30)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            SetsWithNonEmptyRequiresIds = setsWithRequires
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            LegacyDiscoveryFalselyClearedSetIds = falselyCleared,
            LegacyDiscoveryEmptyWithoutRequiresSetIds = emptyWithoutRequires,
            UnusualUnknownFields = unusual,
            Examples = examples
        };
    }

    private static string ClassifyRequiresPattern(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return "Empty";
        }

        if (tokens.Count == 1 && string.Equals(tokens[0], "isPVPMap?", StringComparison.Ordinal))
        {
            return "PvPMapGate";
        }

        if (tokens.Any(token => token.Contains("PowerBoostsSlotted", StringComparison.Ordinal)))
        {
            return "PieceSpecificGate";
        }

        return "Other:" + string.Join(' ', tokens);
    }

    private static NonSetIdentityDiscoveryFacts BuildNonSetFacts(
        HomecomingEnhancementCandidateDocument candidate,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> discoveryBoosts,
        HomecomingMessageStore messages)
    {
        var groups = candidate.Enhancements
            .Where(value => value.EnhancementSetAppOwnedId is null && value.SourceVariants.Count > 1)
            .OrderBy(value => value.AppOwnedId, StringComparer.Ordinal)
            .Select(group => DescribeNonSetGroup(group, discoveryBoosts, messages))
            .ToArray();

        return new NonSetIdentityDiscoveryFacts
        {
            MultiSourceGroupCount = groups.Length,
            ConcreteSourceCount = groups.Sum(value => value.SourceCount),
            CraftedLevelSeriesCount = groups.Count(value => value.Kind == "CraftedLevelSeries"),
            OriginSynonymPairCount = groups.Count(value => value.Kind == "OriginSynonymPair"),
            OtherCount = groups.Count(value => value.Kind == "Other"),
            Groups = groups
        };
    }

    private static NonSetGroupDiscoveryRecord DescribeNonSetGroup(
        HomecomingEnhancementCandidateRecord group,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> discoveryBoosts,
        HomecomingMessageStore messages)
    {
        var sources = group.SourceVariants
            .OrderBy(value => value.HomecomingSourceId, StringComparer.Ordinal)
            .Select(variant =>
            {
                var discovery = discoveryBoosts[variant.HomecomingSourceId];
                messages.TryResolve(discovery.DisplayHelpMessageKey, out var help);
                messages.TryResolve(discovery.ShortHelpMessageKey, out var shortHelp);
                return new NonSetSourceDiscoveryRecord
                {
                    HomecomingSourceId = variant.HomecomingSourceId,
                    SourceForm = variant.SourceForm,
                    Icon = discovery.Icon,
                    NonOriginBoostTypes = discovery.NonOriginBoostTypes.ToArray(),
                    BoostsAllowed = discovery.BoostsAllowed.ToArray(),
                    DisplayHelpMessageKey = discovery.DisplayHelpMessageKey,
                    ShortHelpMessageKey = discovery.ShortHelpMessageKey,
                    DisplayHelp = help,
                    ShortHelp = shortHelp
                };
            })
            .ToArray();

        var ids = sources.Select(value => value.HomecomingSourceId).ToArray();
        var craftedLevel = ids.All(IsCraftedInventionSeriesId);
        var synonymPair = ids.Length == 2 && !craftedLevel;
        var kind = craftedLevel
            ? "CraftedLevelSeries"
            : synonymPair
                ? "OriginSynonymPair"
                : "Other";

        var evidenceClass = craftedLevel
            ? ClassifyCraftedEvidence(sources)
            : synonymPair
                ? ClassifySynonymEvidence(sources)
                : "Unproven";

        var shouldSplit = evidenceClass is "ShouldSplitDistinctMechanics";
        return new NonSetGroupDiscoveryRecord
        {
            AppOwnedId = group.AppOwnedId,
            DisplayName = group.DisplayName,
            Kind = kind,
            SourceCount = sources.Length,
            EvidenceClass = evidenceClass,
            CurrentGroupingProvenCorrect = evidenceClass is
                "NamingConventionCorroboratedByStructure"
                or "SharedStructuralIdentityMatchingMechanics"
                or "SharedBoostTypeWithPresentationVariance",
            ShouldSplit = shouldSplit,
            DistinctNonOriginTypeSets = sources
                .Select(value => string.Join('+', value.NonOriginBoostTypes))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            DistinctIcons = sources.Select(value => value.Icon).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            DistinctHelpKeys = sources.Select(value => value.DisplayHelpMessageKey).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            DistinctBoostsAllowedShapes = sources
                .Select(value => string.Join('|', value.BoostsAllowed.OrderBy(item => item, StringComparer.Ordinal)))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            Sources = sources
        };
    }

    private static string ClassifyCraftedEvidence(IReadOnlyList<NonSetSourceDiscoveryRecord> sources)
    {
        var baseNames = sources
            .Select(value => StripCraftedSeriesBaseId(value.HomecomingSourceId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var typeSets = sources
            .Select(value => string.Join('+', value.NonOriginBoostTypes))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var icons = sources.Select(value => value.Icon).Distinct(StringComparer.Ordinal).ToArray();
        var helps = sources.Select(value => value.DisplayHelpMessageKey).Distinct(StringComparer.Ordinal).ToArray();
        var allowed = sources
            .Select(value => string.Join('|', value.BoostsAllowed.OrderBy(item => item, StringComparer.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (baseNames.Length == 1
            && typeSets.Length == 1
            && icons.Length == 1
            && helps.Length == 1
            && allowed.Length == 1)
        {
            // Deterministic Crafted_<Aspect>[_Level] series + matching structural fields.
            return "NamingConventionCorroboratedByStructure";
        }

        if (typeSets.Length > 1 || allowed.Length > 1)
        {
            return "ShouldSplitDistinctMechanics";
        }

        return "DisplayNameOnly";
    }

    private static string ClassifySynonymEvidence(IReadOnlyList<NonSetSourceDiscoveryRecord> sources)
    {
        var typeSets = sources
            .Select(value => string.Join('+', value.NonOriginBoostTypes))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var icons = sources.Select(value => value.Icon).Distinct(StringComparer.Ordinal).ToArray();
        var helps = sources.Select(value => value.DisplayHelpMessageKey).Distinct(StringComparer.Ordinal).ToArray();
        var allowed = sources
            .Select(value => string.Join('|', value.BoostsAllowed.OrderBy(item => item, StringComparer.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (typeSets.Length > 1 || allowed.Length > 1)
        {
            return "ShouldSplitDistinctMechanics";
        }

        if (typeSets.Length == 1 && icons.Length == 1 && helps.Length == 1 && allowed.Length == 1)
        {
            return "SharedStructuralIdentityMatchingMechanics";
        }

        if (typeSets.Length == 1 && allowed.Length == 1)
        {
            return "SharedBoostTypeWithPresentationVariance";
        }

        return "DisplayNameOnly";
    }

    private static bool IsCraftedLevelVariantId(string sourceId) =>
        Regex.IsMatch(
            sourceId,
            @"^Boosts\.Crafted_.+_\d+\.Crafted_.+_\d+$",
            RegexOptions.CultureInvariant);

    private static bool IsCraftedInventionSeriesId(string sourceId) =>
        Regex.IsMatch(
            sourceId,
            @"^Boosts\.Crafted_[A-Za-z0-9_]+(?:_\d+)?\.Crafted_[A-Za-z0-9_]+(?:_\d+)?$",
            RegexOptions.CultureInvariant)
        && !sourceId.Contains("Hamidon", StringComparison.OrdinalIgnoreCase);

    private static string StripCraftedSeriesBaseId(string sourceId)
    {
        var match = Regex.Match(
            sourceId,
            @"^Boosts\.(Crafted_.+)\.(Crafted_.+)$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return sourceId;
        }

        static string StripTrailingLevel(string value) =>
            Regex.Replace(value, @"_\d+$", string.Empty, RegexOptions.CultureInvariant);

        return $"Boosts.{StripTrailingLevel(match.Groups[1].Value)}.{StripTrailingLevel(match.Groups[2].Value)}";
    }

    private static string StripCraftedLevelSuffix(string sourceId) =>
        StripCraftedSeriesBaseId(sourceId);

    private static FamilyDiscoveryFacts BuildFamilyFacts(
        HomecomingEnhancementCandidateDocument candidate,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> discoveryBoosts)
    {
        var nonSet = candidate.Enhancements
            .Where(value => value.EnhancementSetAppOwnedId is null)
            .ToArray();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var examples = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var enhancement in nonSet)
        {
            var families = enhancement.SourceVariants
                .Select(variant => ClassifyFamily(variant.HomecomingSourceId, discoveryBoosts[variant.HomecomingSourceId]))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var family = families.Length == 1
                ? families[0]
                : "Mixed:" + string.Join('+', families);
            counts[family] = counts.GetValueOrDefault(family) + 1;
            if (!examples.TryGetValue(family, out var list))
            {
                list = [];
                examples[family] = list;
            }

            if (list.Count < 5)
            {
                list.Add($"{enhancement.AppOwnedId}:{enhancement.DisplayName}");
            }
        }

        return new FamilyDiscoveryFacts
        {
            NonSetLogicalCount = nonSet.Length,
            FamilyCounts = counts
                .OrderByDescending(pair => pair.Value)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            FamilyExamples = examples
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal)
        };
    }

    private static string ClassifyFamily(string sourceId, HomecomingBoostDiscoveryRecord discovery)
    {
        if (IsCraftedLevelVariantId(sourceId) ||
            (sourceId.StartsWith("Boosts.Crafted_", StringComparison.Ordinal)
             && discovery.NonOriginBoostTypes.Count == 1
             && discovery.BoostsAllowed.Count(value => OriginBoostTypes.Contains(value, StringComparer.Ordinal)) == 5))
        {
            // Crafted invention IOs: five origins + one aspect type.
            if (sourceId.StartsWith("Boosts.Crafted_", StringComparison.Ordinal)
                && !sourceId.Contains("Hamidon", StringComparison.OrdinalIgnoreCase)
                && discovery.NonOriginBoostTypes.All(value =>
                    !string.Equals(value, "Hamidon", StringComparison.Ordinal)))
            {
                return "CraftedInvention";
            }
        }

        if (discovery.NonOriginBoostTypes.Contains("Hamidon", StringComparer.Ordinal)
            || sourceId.Contains("Hamidon", StringComparison.OrdinalIgnoreCase)
            || sourceId.Contains("Hydra", StringComparison.OrdinalIgnoreCase)
            || sourceId.Contains("Titan", StringComparison.OrdinalIgnoreCase)
            || sourceId.Contains("Yin", StringComparison.OrdinalIgnoreCase)
            || sourceId.Contains("Synthetic", StringComparison.OrdinalIgnoreCase)
            || sourceId.Contains("DSync", StringComparison.OrdinalIgnoreCase)
            || sourceId.Contains("D_Sync", StringComparison.OrdinalIgnoreCase))
        {
            if (sourceId.Contains("Hydra", StringComparison.OrdinalIgnoreCase))
            {
                return "SpecialHydra";
            }

            if (sourceId.Contains("Titan", StringComparison.OrdinalIgnoreCase))
            {
                return "SpecialTitan";
            }

            if (sourceId.Contains("Yin", StringComparison.OrdinalIgnoreCase))
            {
                return "SpecialYin";
            }

            if (sourceId.Contains("Synthetic", StringComparison.OrdinalIgnoreCase))
            {
                return "SpecialSynthetic";
            }

            if (sourceId.Contains("DSync", StringComparison.OrdinalIgnoreCase)
                || sourceId.Contains("D_Sync", StringComparison.OrdinalIgnoreCase))
            {
                return "SpecialDSync";
            }

            return "SpecialHamidonFamily";
        }

        if (discovery.NonOriginBoostTypes.Count == 0)
        {
            return "OriginOrTraining";
        }

        if (discovery.NonOriginBoostTypes.Count == 1
            && discovery.BoostsAllowed.Count(value => OriginBoostTypes.Contains(value, StringComparer.Ordinal)) >= 1
            && (sourceId.StartsWith("Boosts.Generic_", StringComparison.Ordinal)
                || sourceId.StartsWith("Boosts.Magic_", StringComparison.Ordinal)
                || sourceId.StartsWith("Boosts.Mutation_", StringComparison.Ordinal)
                || sourceId.StartsWith("Boosts.Natural_", StringComparison.Ordinal)
                || sourceId.StartsWith("Boosts.Science_", StringComparison.Ordinal)
                || sourceId.StartsWith("Boosts.Technology_", StringComparison.Ordinal)))
        {
            return "OriginOrTraining";
        }

        if (discovery.NonOriginBoostTypes.Count > 1)
        {
            return "SpecialMultiAspect";
        }

        if (sourceId.StartsWith("Boosts.Crafted_", StringComparison.Ordinal))
        {
            return "CraftedInvention";
        }

        return "OtherNonSet";
    }

    private static HistoricalDiscoveryFacts BuildHistoricalFacts(
        HomecomingEnhancementCandidateDocument candidate,
        IReadOnlyList<HomecomingBoostSetRecord> liveSets,
        ItemReferenceCatalog catalog,
        HomecomingMessageStore messages)
    {
        var liveSetIds = liveSets.Select(value => value.HomecomingSetId).ToHashSet(StringComparer.Ordinal);
        var liveEnhIds = candidate.Enhancements
            .Where(value => value.AppOwnedId is not null)
            .Select(value => value.AppOwnedId!)
            .ToHashSet(StringComparer.Ordinal);
        var liveSetAppIds = candidate.EnhancementSets
            .Where(value => value.AppOwnedId is not null)
            .Select(value => value.AppOwnedId!)
            .ToHashSet(StringComparer.Ordinal);

        var historicalSets = catalog.GetEnhancementSets(ReferenceCatalogQueryScope.AllHomecomingIdentities).Values
            .Where(value => !liveSetAppIds.Contains(value.CatalogItemId))
            .OrderBy(value => value.CatalogItemId, StringComparer.Ordinal)
            .Select(value =>
            {
                var liveSpellingMatches = liveSets
                    .Where(live =>
                        string.Equals(
                            NormalizeSpelling(live.HomecomingSetId),
                            NormalizeSpelling(value.HomecomingSetId ?? value.CurrentDisplayName),
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            NormalizeSpelling(ResolveDisplay(messages, live.DisplayNameMessageKey)),
                            NormalizeSpelling(value.CurrentDisplayName),
                            StringComparison.OrdinalIgnoreCase))
                    .Select(live => live.HomecomingSetId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return new HistoricalSetDiscoveryRecord
                {
                    CatalogItemId = value.CatalogItemId,
                    CurrentDisplayName = value.CurrentDisplayName,
                    HomecomingSetId = value.HomecomingSetId,
                    ActiveStatus = value.ActiveStatus.ToString(),
                    BonusCount = value.Bonuses.Count,
                    NearSpellLiveSetIds = liveSpellingMatches,
                    Classification = ClassifyHistoricalSet(value, liveSetIds, liveSpellingMatches)
                };
            })
            .ToArray();

        var historicalEnhancements = catalog.DebugItems.Values
            .Where(value =>
                value.Family == ReferenceItemFamily.Enhancement
                && !liveEnhIds.Contains(value.CatalogItemId))
            .OrderBy(value => value.CatalogItemId, StringComparer.Ordinal)
            .Select(value =>
            {
                string classification;
                if (value.EnhancementSetId is not null
                    && historicalSets.Any(set => set.CatalogItemId == value.EnhancementSetId))
                {
                    classification = "PieceOfHistoricalSet:" + value.EnhancementSetId;
                }
                else if (value.EnhancementSetId is not null
                         && liveSetAppIds.Contains(value.EnhancementSetId))
                {
                    classification = "SetPieceMissingFromLiveLogical:" + value.EnhancementSetId;
                }
                else if (value.CurrentDisplayName.StartsWith("Invention:", StringComparison.Ordinal))
                {
                    classification = "LegacyNonSetInventionNamed";
                }
                else
                {
                    classification = "LegacyNonSetOther";
                }

                return new HistoricalEnhancementDiscoveryRecord
                {
                    CatalogItemId = value.CatalogItemId,
                    CurrentDisplayName = value.CurrentDisplayName,
                    Subtype = value.Subtype,
                    EnhancementSetId = value.EnhancementSetId,
                    ActiveStatus = value.ActiveStatus.ToString(),
                    Classification = classification
                };
            })
            .ToArray();

        return new HistoricalDiscoveryFacts
        {
            HistoricalSetCount = historicalSets.Length,
            HistoricalEnhancementCount = historicalEnhancements.Length,
            HistoricalSets = historicalSets,
            ClassificationCounts = historicalEnhancements
                .GroupBy(value => value.Classification.Split(':')[0], StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            HistoricalEnhancements = historicalEnhancements
        };
    }

    private static string ClassifyHistoricalSet(
        EnhancementSetReferenceRecord value,
        HashSet<string> liveSetIds,
        IReadOnlyList<string> nearSpellLiveSetIds)
    {
        if (value.HomecomingSetId is not null && liveSetIds.Contains(value.HomecomingSetId))
        {
            return "UnexpectedLiveHomecomingIdUnmatchedByAppId";
        }

        if (nearSpellLiveSetIds.Count > 0)
        {
            if (nearSpellLiveSetIds.Any(id =>
                    id.Contains("Ascendency", StringComparison.Ordinal)
                    || id.Contains("Ascendancy", StringComparison.Ordinal)))
            {
                return "OrphanedDuplicateOfLiveSetDueToDisplayDrift:" + string.Join(',', nearSpellLiveSetIds);
            }

            return "SpellingOrIdentityReplacement:" + string.Join(',', nearSpellLiveSetIds);
        }

        if (string.Equals(value.CurrentDisplayName, "Bands of Hermes", StringComparison.Ordinal))
        {
            return "RemovedOrAbsentLiveSet";
        }

        return "AbsentFromLiveBoostsets";
    }

    private static string NormalizeSpelling(string value) =>
        new string(value
            .Replace('_', ' ')
            .Where(character => !char.IsWhiteSpace(character))
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static string ResolveDisplay(HomecomingMessageStore messages, string key) =>
        messages.TryResolve(key, out var value) ? value : key;

    private static VariantHelpDiscoveryFacts BuildHelpFacts(
        HomecomingEnhancementCandidateDocument candidate,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> discoveryBoosts,
        HomecomingMessageStore messages)
    {
        var disagreements = new List<VariantHelpDisagreementRecord>();
        foreach (var enhancement in candidate.Enhancements.OrderBy(value => value.AppOwnedId))
        {
            var helpKeys = enhancement.SourceVariants
                .Select(variant => discoveryBoosts[variant.HomecomingSourceId].DisplayHelpMessageKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (helpKeys.Length <= 1)
            {
                continue;
            }

            var variants = enhancement.SourceVariants
                .Select(variant =>
                {
                    var discovery = discoveryBoosts[variant.HomecomingSourceId];
                    messages.TryResolve(discovery.DisplayHelpMessageKey, out var help);
                    messages.TryResolve(discovery.ShortHelpMessageKey, out var shortHelp);
                    return new VariantHelpVariantRecord
                    {
                        HomecomingSourceId = variant.HomecomingSourceId,
                        SourceForm = variant.SourceForm,
                        DisplayHelpMessageKey = discovery.DisplayHelpMessageKey,
                        DisplayHelp = help,
                        ShortHelpMessageKey = discovery.ShortHelpMessageKey,
                        ShortHelp = shortHelp
                    };
                })
                .ToArray();

            disagreements.Add(new VariantHelpDisagreementRecord
            {
                AppOwnedId = enhancement.AppOwnedId,
                DisplayName = enhancement.DisplayName,
                EnhancementSetAppOwnedId = enhancement.EnhancementSetAppOwnedId,
                DistinctHelpKeyCount = helpKeys.Length,
                Forms = variants.Select(value => value.SourceForm).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                Classification = ClassifyHelpDisagreement(variants),
                Variants = variants
            });
        }

        return new VariantHelpDiscoveryFacts
        {
            DisagreementGroupCount = disagreements.Count,
            ClassificationCounts = disagreements
                .GroupBy(value => value.Classification, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            Disagreements = disagreements
        };
    }

    private static string ClassifyHelpDisagreement(IReadOnlyList<VariantHelpVariantRecord> variants)
    {
        var forms = variants.Select(value => value.SourceForm).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (forms.SetEquals(["Crafted", "Attuned"])
            || forms.SetEquals(["Crafted", "Superior_Attuned"])
            || forms.SetEquals(["Crafted", "Attuned", "Superior_Attuned"]))
        {
            return "CraftedVsAttunedWording";
        }

        var texts = variants
            .Select(value => value.DisplayHelp ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (texts.Length > 1
            && texts.All(text => text.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                                 || text.Contains("{Boost.", StringComparison.Ordinal)))
        {
            return "VariantTemplateWording";
        }

        return "OtherHelpDivergence";
    }

    private static bool TryParseArgs(
        string[] args,
        out string installRoot,
        out string reportPath,
        out string failureReason)
    {
        installRoot = string.Empty;
        reportPath = string.Empty;
        failureReason = string.Empty;
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (option is "--install" && index + 1 < args.Length)
            {
                installRoot = args[++index];
                continue;
            }

            if (option is "--out" && index + 1 < args.Length)
            {
                reportPath = args[++index];
                continue;
            }

            failureReason = $"Unknown discover-enhancement-repair-facts option '{option}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(installRoot) || string.IsNullOrWhiteSpace(reportPath))
        {
            failureReason = "Both --install and --out are required.";
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> SnapshotHashes(string installRoot)
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(installRoot);
        return
        [
            $"{source.BinPiggPath}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.BinPiggPath)))}",
            $"{source.BinPowersPiggPath}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.BinPowersPiggPath)))}"
        ];
    }

    private static void WriteSummary(
        TextWriter output,
        EnhancementRepairDiscoveryReport report,
        string reportPath)
    {
        output.WriteLine("Enhancement repair discovery: PASS");
        output.WriteLine($"Report: {reportPath}");
        output.WriteLine($"Build: {report.BuildVersion}");
        output.WriteLine($"Package: {report.PackageRevision}");
        output.WriteLine(
            $"Bonus tiers={report.Bonus.TotalBonusTiers} nonemptyRequires={report.Bonus.TiersWithNonEmptyRequires} setsWithRequires={report.Bonus.SetsWithNonEmptyRequires}");
        output.WriteLine(
            $"Legacy falsely cleared sets={report.Bonus.LegacyDiscoveryFalselyClearedSetIds.Length}");
        output.WriteLine(
            $"Non-set multi groups={report.NonSetIdentity.MultiSourceGroupCount} crafted={report.NonSetIdentity.CraftedLevelSeriesCount} synonym={report.NonSetIdentity.OriginSynonymPairCount}");
        output.WriteLine(
            $"Historical sets={report.Historical.HistoricalSetCount} historical enh={report.Historical.HistoricalEnhancementCount}");
        output.WriteLine($"Help disagreements={report.VariantHelp.DisagreementGroupCount}");
        output.WriteLine($"Homecoming hashes unchanged: {report.HomecomingHashesUnchanged}");
    }
}

internal sealed class EnhancementRepairDiscoveryReport
{
    public string BuildVersion { get; set; } = "";
    public string PackageRevision { get; set; } = "";
    public bool HomecomingHashesUnchanged { get; set; }
    public string BinPiggSha256 { get; set; } = "";
    public string BinPowersPiggSha256 { get; set; } = "";
    public int LiveSetCount { get; set; }
    public int LiveConcreteBoostCount { get; set; }
    public int LiveLogicalEnhancementCount { get; set; }
    public BonusRequiresDiscoveryFacts Bonus { get; set; } = new();
    public NonSetIdentityDiscoveryFacts NonSetIdentity { get; set; } = new();
    public FamilyDiscoveryFacts Families { get; set; } = new();
    public HistoricalDiscoveryFacts Historical { get; set; } = new();
    public VariantHelpDiscoveryFacts VariantHelp { get; set; } = new();
}

internal sealed class BonusRequiresDiscoveryFacts
{
    public int TotalSets { get; set; }
    public int TotalBonusTiers { get; set; }
    public int TotalAutoPowerRelationships { get; set; }
    public int TiersWithNonEmptyRequires { get; set; }
    public int SetsWithNonEmptyRequires { get; set; }
    public int TotalRequiresTokens { get; set; }
    public int UniqueRequiresOffsets { get; set; }
    public Dictionary<string, int> RequiresLengthDistribution { get; set; } = new();
    public Dictionary<string, int> PatternCounts { get; set; } = new();
    public Dictionary<string, int> TopTokens { get; set; } = new();
    public string[] SetsWithNonEmptyRequiresIds { get; set; } = [];
    public string[] LegacyDiscoveryFalselyClearedSetIds { get; set; } = [];
    public string[] LegacyDiscoveryEmptyWithoutRequiresSetIds { get; set; } = [];
    public List<string> UnusualUnknownFields { get; set; } = [];
    public List<BonusRequiresExample> Examples { get; set; } = [];
}

internal sealed class BonusRequiresExample
{
    public string HomecomingSetId { get; set; } = "";
    public int BonusCount { get; set; }
    public BonusRequiresTierExample[] NonEmptyRequiresTiers { get; set; } = [];
}

internal sealed class BonusRequiresTierExample
{
    public int BonusIndex { get; set; }
    public uint MinimumBoosts { get; set; }
    public uint MaximumBoosts { get; set; }
    public string[] RequiresTokens { get; set; } = [];
    public string[] AutoPowerSourceIds { get; set; } = [];
    public string Pattern { get; set; } = "";
}

internal sealed class NonSetIdentityDiscoveryFacts
{
    public int MultiSourceGroupCount { get; set; }
    public int ConcreteSourceCount { get; set; }
    public int CraftedLevelSeriesCount { get; set; }
    public int OriginSynonymPairCount { get; set; }
    public int OtherCount { get; set; }
    public NonSetGroupDiscoveryRecord[] Groups { get; set; } = [];
}

internal sealed class NonSetGroupDiscoveryRecord
{
    public string? AppOwnedId { get; set; }
    public string DisplayName { get; set; } = "";
    public string Kind { get; set; } = "";
    public int SourceCount { get; set; }
    public string EvidenceClass { get; set; } = "";
    public bool CurrentGroupingProvenCorrect { get; set; }
    public bool ShouldSplit { get; set; }
    public string[] DistinctNonOriginTypeSets { get; set; } = [];
    public string[] DistinctIcons { get; set; } = [];
    public string[] DistinctHelpKeys { get; set; } = [];
    public string[] DistinctBoostsAllowedShapes { get; set; } = [];
    public NonSetSourceDiscoveryRecord[] Sources { get; set; } = [];
}

internal sealed class NonSetSourceDiscoveryRecord
{
    public string HomecomingSourceId { get; set; } = "";
    public string SourceForm { get; set; } = "";
    public string Icon { get; set; } = "";
    public string[] NonOriginBoostTypes { get; set; } = [];
    public string[] BoostsAllowed { get; set; } = [];
    public string DisplayHelpMessageKey { get; set; } = "";
    public string? ShortHelpMessageKey { get; set; }
    public string? DisplayHelp { get; set; }
    public string? ShortHelp { get; set; }
}

internal sealed class FamilyDiscoveryFacts
{
    public int NonSetLogicalCount { get; set; }
    public Dictionary<string, int> FamilyCounts { get; set; } = new();
    public Dictionary<string, string[]> FamilyExamples { get; set; } = new();
}

internal sealed class HistoricalDiscoveryFacts
{
    public int HistoricalSetCount { get; set; }
    public int HistoricalEnhancementCount { get; set; }
    public HistoricalSetDiscoveryRecord[] HistoricalSets { get; set; } = [];
    public Dictionary<string, int> ClassificationCounts { get; set; } = new();
    public HistoricalEnhancementDiscoveryRecord[] HistoricalEnhancements { get; set; } = [];
}

internal sealed class HistoricalSetDiscoveryRecord
{
    public string CatalogItemId { get; set; } = "";
    public string CurrentDisplayName { get; set; } = "";
    public string? HomecomingSetId { get; set; }
    public string ActiveStatus { get; set; } = "";
    public int BonusCount { get; set; }
    public string[] NearSpellLiveSetIds { get; set; } = [];
    public string Classification { get; set; } = "";
}

internal sealed class HistoricalEnhancementDiscoveryRecord
{
    public string CatalogItemId { get; set; } = "";
    public string CurrentDisplayName { get; set; } = "";
    public string Subtype { get; set; } = "";
    public string? EnhancementSetId { get; set; }
    public string ActiveStatus { get; set; } = "";
    public string Classification { get; set; } = "";
}

internal sealed class VariantHelpDiscoveryFacts
{
    public int DisagreementGroupCount { get; set; }
    public Dictionary<string, int> ClassificationCounts { get; set; } = new();
    public List<VariantHelpDisagreementRecord> Disagreements { get; set; } = [];
}

internal sealed class VariantHelpDisagreementRecord
{
    public string? AppOwnedId { get; set; }
    public string DisplayName { get; set; } = "";
    public string? EnhancementSetAppOwnedId { get; set; }
    public int DistinctHelpKeyCount { get; set; }
    public string[] Forms { get; set; } = [];
    public string Classification { get; set; } = "";
    public VariantHelpVariantRecord[] Variants { get; set; } = [];
}

internal sealed class VariantHelpVariantRecord
{
    public string HomecomingSourceId { get; set; } = "";
    public string SourceForm { get; set; } = "";
    public string DisplayHelpMessageKey { get; set; } = "";
    public string? DisplayHelp { get; set; }
    public string? ShortHelpMessageKey { get; set; }
    public string? ShortHelp { get; set; }
}

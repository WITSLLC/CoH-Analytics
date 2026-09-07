using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingEnhancementCandidateGenerator
{
    private const string BoostSetsPvpRarityCode = "ECPvP";
    private const string CanonicalPvpRarityCode = "ECPVP";
    private const string UnresolvedToHitDebuffCategoryCode = "ECToHitDeBuff";

    internal const string SchemaVersion = "homecoming-enhancement-candidate-v1";
    internal const string PowersArchive = "assets/live/bin_powers.pigg";
    internal const string PowersMember = "bin/powers.bin";
    internal const string BoostSetsArchive = "assets/live/bin.pigg";
    internal const string BoostSetsMember = "bin/boostsets.bin";

    internal static HomecomingEnhancementCandidateDocument Create(
        IReadOnlyList<HomecomingConcreteBoostRecord> concreteBoosts,
        IReadOnlyList<HomecomingBoostSetRecord> boostSets,
        HomecomingMessageStore messageStore,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> discoveryBoosts,
        IReadOnlyList<CurrentEnhancementIdentity> currentEnhancements,
        IReadOnlyList<CurrentEnhancementSetIdentity> currentSets,
        string buildVersion,
        string packageRevision)
    {
        ArgumentNullException.ThrowIfNull(concreteBoosts);
        ArgumentNullException.ThrowIfNull(boostSets);
        ArgumentNullException.ThrowIfNull(messageStore);
        ArgumentNullException.ThrowIfNull(discoveryBoosts);
        ArgumentNullException.ThrowIfNull(currentEnhancements);
        ArgumentNullException.ThrowIfNull(currentSets);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRevision);

        var resolvedBoosts = ResolveBoosts(concreteBoosts, messageStore);
        var resolvedSets = ResolveSets(boostSets, messageStore);
        var setResult = MatchSets(resolvedSets, currentSets);
        var logicalEnhancements = GroupEnhancements(
            resolvedBoosts,
            resolvedSets,
            setResult.Candidates,
            discoveryBoosts);
        var enhancementResult = MatchEnhancements(
            logicalEnhancements,
            currentEnhancements,
            setResult.Candidates,
            discoveryBoosts);

        return new HomecomingEnhancementCandidateDocument(
            SchemaVersion,
            new HomecomingEnhancementCandidateSource(
                buildVersion,
                packageRevision,
                PowersArchive,
                PowersMember,
                BoostSetsArchive,
                BoostSetsMember),
            CreateSetSummary(setResult.Candidates, setResult.CurrentOnly),
            CreateEnhancementSummary(
                concreteBoosts.Count,
                enhancementResult.Candidates,
                enhancementResult.CurrentOnly),
            setResult.Candidates,
            enhancementResult.Candidates,
            setResult.CurrentOnly,
            enhancementResult.CurrentOnly);
    }

    internal static HomecomingCurrentEnhancementCatalog LoadEmbeddedCurrentCatalog()
    {
        var assembly = typeof(ItemReferenceCatalogFactory).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            ItemReferenceCatalogFactory.ProductionCatalogResourceName)
            ?? throw new HomecomingEnhancementCandidateException(
                "Embedded production item catalog was not found.");
        using var document = JsonDocument.Parse(stream);

        var enhancements = new List<CurrentEnhancementIdentity>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(
                    item.GetProperty("family").GetString(),
                    "Enhancement",
                    StringComparison.Ordinal))
            {
                continue;
            }

            enhancements.Add(new CurrentEnhancementIdentity(
                RequireString(item, "catalogItemId"),
                RequireString(item, "currentDisplayName"),
                OptionalString(item, "enhancementSetId"),
                ReadSourceVariantIds(item)));
        }

        var sets = document.RootElement
            .GetProperty("enhancementSets")
            .EnumerateArray()
            .Select(item => new CurrentEnhancementSetIdentity(
                RequireString(item, "catalogItemId"),
                RequireString(item, "currentDisplayName")))
            .ToArray();
        return new HomecomingCurrentEnhancementCatalog(enhancements, sets);
    }

    private static IReadOnlyList<ResolvedConcreteBoost> ResolveBoosts(
        IReadOnlyList<HomecomingConcreteBoostRecord> concreteBoosts,
        HomecomingMessageStore messageStore)
    {
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var resolved = new List<ResolvedConcreteBoost>(concreteBoosts.Count);
        foreach (var boost in concreteBoosts.OrderBy(
                     value => value.HomecomingSourceId,
                     StringComparer.Ordinal))
        {
            if (!sourceIds.Add(boost.HomecomingSourceId))
            {
                throw new HomecomingEnhancementCandidateException(
                    $"Boost source ID '{boost.HomecomingSourceId}' is duplicated.");
            }

            var displayName = ResolveRequiredMessage(
                messageStore,
                boost.DisplayNameMessageKey,
                $"Boost '{boost.HomecomingSourceId}' display name");
            resolved.Add(new ResolvedConcreteBoost(
                boost.HomecomingSourceId,
                boost.DisplayNameMessageKey,
                displayName,
                boost.SourceForm));
        }

        return resolved;
    }

    private static IReadOnlyList<ResolvedBoostSet> ResolveSets(
        IReadOnlyList<HomecomingBoostSetRecord> boostSets,
        HomecomingMessageStore messageStore)
    {
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var orderedSets = boostSets
            .OrderBy(value => value.HomecomingSetId, StringComparer.Ordinal)
            .ToArray();
        foreach (var set in orderedSets)
        {
            if (!sourceIds.Add(set.HomecomingSetId))
            {
                throw new HomecomingEnhancementCandidateException(
                    $"Boost Set source ID '{set.HomecomingSetId}' is duplicated.");
            }
        }

        var classifiedSets = orderedSets
            .Select(value => ClassifySetConversionCodes(value, messageStore))
            .ToArray();
        var categoryCodesByDescriptionKey = classifiedSets
            .Where(value => value.CategoryCode is not null)
            .GroupBy(value => value.Source.DescriptionMessageKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(value => value.CategoryCode!)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var resolved = new List<ResolvedBoostSet>(classifiedSets.Length);
        foreach (var classified in classifiedSets)
        {
            var set = classified.Source;
            var displayName = ResolveRequiredMessage(
                messageStore,
                set.DisplayNameMessageKey,
                $"Boost Set '{set.HomecomingSetId}' display name");
            var categoryCode = classified.CategoryCode;
            var categoryCodeSource = "ConversionCode";
            if (categoryCode is null)
            {
                if (!categoryCodesByDescriptionKey.TryGetValue(
                        set.DescriptionMessageKey,
                        out var exactCodes)
                    || exactCodes.Length == 0)
                {
                    throw new HomecomingEnhancementCandidateException(
                        $"Boost Set '{set.HomecomingSetId}' category code could not be resolved " +
                        "through its exact Homecoming description message key.");
                }

                if (exactCodes.Length > 1)
                {
                    throw new HomecomingEnhancementCandidateException(
                        $"Boost Set '{set.HomecomingSetId}' category description maps to multiple " +
                        $"Homecoming category codes: {string.Join(", ", exactCodes)}.");
                }

                categoryCode = exactCodes[0];
                categoryCodeSource = "ExactDescriptionMessageKeyJoin";
            }

            resolved.Add(new ResolvedBoostSet(
                set,
                displayName,
                classified.RarityCode,
                classified.RarityDisplayText,
                categoryCode,
                ResolveOptionalMessage(messageStore, categoryCode),
                categoryCodeSource));
        }

        return resolved;
    }

    private static ClassifiedBoostSet ClassifySetConversionCodes(
        HomecomingBoostSetRecord set,
        HomecomingMessageStore messageStore)
    {
        string? rarityCode = null;
        string? rarityDisplayText = null;
        string? categoryCode = null;
        foreach (var rawCode in set.ConversionCodes)
        {
            var code = NormalizeSetConversionCode(rawCode);
            var displayText = ResolveOptionalMessage(messageStore, code);
            var kind = displayText switch
            {
                not null when displayText.StartsWith("Rarity:", StringComparison.Ordinal) =>
                    HomecomingSetConversionCodeKind.Rarity,
                not null when displayText.StartsWith("Category:", StringComparison.Ordinal) =>
                    HomecomingSetConversionCodeKind.Category,
                _ when code == UnresolvedToHitDebuffCategoryCode =>
                    HomecomingSetConversionCodeKind.Category,
                _ => throw new HomecomingEnhancementCandidateException(
                    $"Boost Set '{set.HomecomingSetId}' has unknown conversion code '{rawCode}'.")
            };

            if (kind == HomecomingSetConversionCodeKind.Rarity)
            {
                if (rarityCode is not null)
                {
                    throw new HomecomingEnhancementCandidateException(
                        $"Boost Set '{set.HomecomingSetId}' has multiple rarity codes.");
                }

                rarityCode = code;
                rarityDisplayText = displayText;
            }
            else
            {
                if (categoryCode is not null)
                {
                    throw new HomecomingEnhancementCandidateException(
                        $"Boost Set '{set.HomecomingSetId}' has multiple category codes.");
                }

                categoryCode = code;
            }
        }

        return new ClassifiedBoostSet(set, rarityCode, rarityDisplayText, categoryCode);
    }

    private static SetMatchResult MatchSets(
        IReadOnlyList<ResolvedBoostSet> sets,
        IReadOnlyList<CurrentEnhancementSetIdentity> currentSets)
    {
        var maximumId = ValidateCurrentSetCatalog(currentSets);
        var currentByName = currentSets
            .GroupBy(value => value.DisplayName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var sourceByName = sets
            .GroupBy(value => value.DisplayName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var nextId = maximumId;
        var candidates = new List<HomecomingEnhancementSetCandidateRecord>(sets.Count);
        foreach (var set in sets)
        {
            currentByName.TryGetValue(set.DisplayName, out var currentMatches);
            currentMatches ??= [];
            if (currentMatches.Length == 1 && sourceByName[set.DisplayName].Length == 1)
            {
                candidates.Add(ToSetCandidate(
                    set,
                    currentMatches[0].AppOwnedId,
                    HomecomingEnhancementMatchStatus.MatchedExisting,
                    [currentMatches[0].AppOwnedId]));
                continue;
            }

            if (currentMatches.Length > 0)
            {
                candidates.Add(ToSetCandidate(
                    set,
                    null,
                    HomecomingEnhancementMatchStatus.Ambiguous,
                    currentMatches
                        .Select(value => value.AppOwnedId)
                        .Order(StringComparer.Ordinal)
                        .ToArray()));
                continue;
            }

            nextId = NextId(nextId, "SET");
            candidates.Add(ToSetCandidate(
                set,
                $"SET-{nextId:D5}",
                HomecomingEnhancementMatchStatus.NewFromHomecoming,
                []));
        }

        var representedCurrentIds = candidates
            .SelectMany(value => value.MatchingExistingAppOwnedIds)
            .ToHashSet(StringComparer.Ordinal);
        var currentOnly = currentSets
            .Where(value => !representedCurrentIds.Contains(value.AppOwnedId))
            .OrderBy(value => value.AppOwnedId, StringComparer.Ordinal)
            .Select(value => new HomecomingCurrentCatalogIdentityRecord(
                value.AppOwnedId,
                value.DisplayName))
            .ToArray();
        return new SetMatchResult(candidates, currentOnly);
    }

    private static IReadOnlyList<LogicalEnhancement> GroupEnhancements(
        IReadOnlyList<ResolvedConcreteBoost> boosts,
        IReadOnlyList<ResolvedBoostSet> sets,
        IReadOnlyList<HomecomingEnhancementSetCandidateRecord> setCandidates,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> discoveryBoosts)
    {
        var boostsById = boosts.ToDictionary(
            value => value.HomecomingSourceId,
            StringComparer.Ordinal);
        var setCandidatesById = setCandidates.ToDictionary(
            value => value.HomecomingSetId,
            StringComparer.Ordinal);
        var assigned = new Dictionary<string, string>(StringComparer.Ordinal);
        var logical = new List<LogicalEnhancement>();

        foreach (var set in sets)
        {
            if (!setCandidatesById.TryGetValue(set.Source.HomecomingSetId, out var setCandidate))
            {
                throw new HomecomingEnhancementCandidateException(
                    $"Boost Set '{set.Source.HomecomingSetId}' has no candidate identity.");
            }

            for (var groupIndex = 0; groupIndex < set.Source.MemberGroups.Count; groupIndex++)
            {
                var members = new List<ResolvedConcreteBoost>();
                foreach (var memberId in set.Source.MemberGroups[groupIndex])
                {
                    if (!boostsById.TryGetValue(memberId, out var boost))
                    {
                        throw new HomecomingEnhancementCandidateException(
                            $"Boost Set '{set.Source.HomecomingSetId}' member '{memberId}' " +
                            "was not found in powers.bin.");
                    }

                    var owner = $"{set.Source.HomecomingSetId} member group {groupIndex}";
                    if (assigned.TryGetValue(memberId, out var existingOwner))
                    {
                        throw new HomecomingEnhancementCandidateException(
                            $"Boost source '{memberId}' belongs to conflicting logical identities " +
                            $"'{existingOwner}' and '{owner}'.");
                    }

                    assigned.Add(memberId, owner);
                    members.Add(boost);
                }

                var orderedMembers = members
                    .OrderBy(value => value.HomecomingSourceId, StringComparer.Ordinal)
                    .ToArray();
                var displayNames = orderedMembers
                    .Select(value => value.DisplayName)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (displayNames.Length != 1)
                {
                    throw new HomecomingEnhancementCandidateException(
                        $"Boost Set '{set.Source.HomecomingSetId}' member group {groupIndex} " +
                        "contains conflicting player-facing names.");
                }

                logical.Add(new LogicalEnhancement(
                    displayNames[0],
                    set.Source.HomecomingSetId,
                    setCandidate.AppOwnedId,
                    orderedMembers));
            }
        }

        var unassigned = boosts
            .Where(value => !assigned.ContainsKey(value.HomecomingSourceId))
            .ToArray();
        var displayNamesBySourceId = boosts.ToDictionary(
            value => value.HomecomingSourceId,
            value => value.DisplayName,
            StringComparer.Ordinal);
        var boostsBySourceId = boosts.ToDictionary(
            value => value.HomecomingSourceId,
            StringComparer.Ordinal);
        logical.AddRange(HomecomingNonSetEnhancementIdentitySupport
            .GroupUnassignedSources(
                unassigned.Select(value => value.HomecomingSourceId).ToArray(),
                discoveryBoosts,
                displayNamesBySourceId)
            .Select(group => new LogicalEnhancement(
                group.RepresentativeDisplayName,
                null,
                null,
                group.SourceIds
                    .Select(sourceId => boostsBySourceId[sourceId])
                    .ToArray())));
        return logical
            .OrderBy(value => value.SourceVariants[0].HomecomingSourceId, StringComparer.Ordinal)
            .ToArray();
    }

    private static EnhancementMatchResult MatchEnhancements(
        IReadOnlyList<LogicalEnhancement> logicalEnhancements,
        IReadOnlyList<CurrentEnhancementIdentity> currentEnhancements,
        IReadOnlyList<HomecomingEnhancementSetCandidateRecord> setCandidates,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> discoveryBoosts)
    {
        var maximumId = ValidateCurrentEnhancementCatalog(currentEnhancements);
        var currentBySetAndName = currentEnhancements
            .Where(value => value.EnhancementSetId is not null)
            .GroupBy(value => (value.EnhancementSetId!, value.DisplayName))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var currentByName = currentEnhancements
            .GroupBy(value => value.DisplayName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var currentNonSetByStructural = BuildCurrentNonSetStructuralIndex(
            currentEnhancements,
            discoveryBoosts);
        var logicalBySetAndName = logicalEnhancements
            .Where(value => value.EnhancementSetAppOwnedId is not null)
            .GroupBy(value => (value.EnhancementSetAppOwnedId!, value.DisplayName))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var logicalNonSetByStructural = logicalEnhancements
            .Where(value => value.EnhancementSetAppOwnedId is null)
            .GroupBy(value => HomecomingNonSetEnhancementIdentitySupport.ResolveLogicalGroupIdentity(
                value.SourceVariants.Select(item => item.HomecomingSourceId).ToArray(),
                discoveryBoosts))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var ambiguousSetIds = setCandidates
            .Where(value => value.MatchStatus == nameof(HomecomingEnhancementMatchStatus.Ambiguous))
            .Select(value => value.HomecomingSetId)
            .ToHashSet(StringComparer.Ordinal);

        var nextId = maximumId;
        var matchedCurrentIds = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<HomecomingEnhancementCandidateRecord>(logicalEnhancements.Count);
        foreach (var logical in logicalEnhancements)
        {
            CurrentEnhancementIdentity[] currentMatches;
            var sourceMatchCount = 1;
            var forceAmbiguous = logical.HomecomingSetId is not null
                && ambiguousSetIds.Contains(logical.HomecomingSetId);
            if (logical.EnhancementSetAppOwnedId is not null)
            {
                var key = (logical.EnhancementSetAppOwnedId, logical.DisplayName);
                currentBySetAndName.TryGetValue(key, out currentMatches!);
                currentMatches ??= [];
                sourceMatchCount = logicalBySetAndName[key].Length;
            }
            else
            {
                if (logical.HomecomingSetId is not null)
                {
                    currentByName.TryGetValue(logical.DisplayName, out currentMatches!);
                    currentMatches ??= [];
                    sourceMatchCount = logicalEnhancements.Count(value =>
                        string.Equals(value.DisplayName, logical.DisplayName, StringComparison.Ordinal));
                }
                else
                {
                    var structuralIdentity = HomecomingNonSetEnhancementIdentitySupport.ResolveLogicalGroupIdentity(
                        logical.SourceVariants.Select(item => item.HomecomingSourceId).ToArray(),
                        discoveryBoosts);
                    currentNonSetByStructural.TryGetValue(structuralIdentity, out currentMatches!);
                    currentMatches ??= [];
                    sourceMatchCount = logicalNonSetByStructural[structuralIdentity].Length;
                }
            }

            if (!forceAmbiguous && currentMatches.Length == 1 && sourceMatchCount == 1)
            {
                if (!matchedCurrentIds.Add(currentMatches[0].AppOwnedId))
                {
                    throw new HomecomingEnhancementCandidateException(
                        $"Current Enhancement '{currentMatches[0].AppOwnedId}' matched more than one " +
                        "Homecoming logical identity.");
                }

                candidates.Add(ToEnhancementCandidate(
                    logical,
                    currentMatches[0].AppOwnedId,
                    HomecomingEnhancementMatchStatus.MatchedExisting,
                    [currentMatches[0].AppOwnedId]));
                continue;
            }

            if (forceAmbiguous || currentMatches.Length > 0)
            {
                candidates.Add(ToEnhancementCandidate(
                    logical,
                    null,
                    HomecomingEnhancementMatchStatus.Ambiguous,
                    currentMatches
                        .Select(value => value.AppOwnedId)
                        .Order(StringComparer.Ordinal)
                        .ToArray()));
                continue;
            }

            nextId = NextId(nextId, "ENH");
            candidates.Add(ToEnhancementCandidate(
                logical,
                $"ENH-{nextId:D5}",
                HomecomingEnhancementMatchStatus.NewFromHomecoming,
                []));
        }

        var representedCurrentIds = candidates
            .SelectMany(value => value.MatchingExistingAppOwnedIds)
            .ToHashSet(StringComparer.Ordinal);
        var currentOnly = currentEnhancements
            .Where(value => !representedCurrentIds.Contains(value.AppOwnedId))
            .OrderBy(value => value.AppOwnedId, StringComparer.Ordinal)
            .Select(value => new HomecomingCurrentEnhancementRecord(
                value.AppOwnedId,
                value.DisplayName,
                value.EnhancementSetId))
            .ToArray();
        return new EnhancementMatchResult(candidates, currentOnly);
    }

    private static HomecomingEnhancementSetCandidateRecord ToSetCandidate(
        ResolvedBoostSet set,
        string? appOwnedId,
        HomecomingEnhancementMatchStatus status,
        IReadOnlyList<string> matchingExistingIds) =>
        new(
            appOwnedId,
            set.Source.HomecomingSetId,
            set.Source.DisplayNameMessageKey,
            set.DisplayName,
            set.RarityCode,
            set.RarityDisplayText,
            set.CategoryCode,
            set.CategoryDisplayText,
            set.CategoryCodeSource,
            set.Source.MinimumLevel,
            set.Source.MaximumLevel,
            status.ToString(),
            matchingExistingIds);

    private static HomecomingEnhancementCandidateRecord ToEnhancementCandidate(
        LogicalEnhancement logical,
        string? appOwnedId,
        HomecomingEnhancementMatchStatus status,
        IReadOnlyList<string> matchingExistingIds) =>
        new(
            appOwnedId,
            logical.DisplayName,
            logical.HomecomingSetId,
            logical.EnhancementSetAppOwnedId,
            status.ToString(),
            matchingExistingIds,
            logical.SourceVariants
                .Select(value => new HomecomingEnhancementSourceVariant(
                    value.HomecomingSourceId,
                    value.DisplayNameMessageKey,
                    value.SourceForm))
                .ToArray());

    private static HomecomingEnhancementSetCandidateSummary CreateSetSummary(
        IReadOnlyList<HomecomingEnhancementSetCandidateRecord> candidates,
        IReadOnlyList<HomecomingCurrentCatalogIdentityRecord> currentOnly) =>
        new(
            candidates.Count,
            candidates.Count,
            CountStatus(candidates.Select(value => value.MatchStatus), HomecomingEnhancementMatchStatus.MatchedExisting),
            CountStatus(candidates.Select(value => value.MatchStatus), HomecomingEnhancementMatchStatus.NewFromHomecoming),
            currentOnly.Count,
            CountStatus(candidates.Select(value => value.MatchStatus), HomecomingEnhancementMatchStatus.Ambiguous),
            CountCodes(candidates.Select(value => value.RarityCode)),
            CountCodes(candidates.Select(value => value.CategoryCode)));

    private static HomecomingEnhancementCandidateSummary CreateEnhancementSummary(
        int concreteBoostCount,
        IReadOnlyList<HomecomingEnhancementCandidateRecord> candidates,
        IReadOnlyList<HomecomingCurrentEnhancementRecord> currentOnly) =>
        new(
            concreteBoostCount,
            concreteBoostCount,
            candidates.Count,
            candidates.Count(value => value.SourceVariants.Count > 1),
            CountStatus(candidates.Select(value => value.MatchStatus), HomecomingEnhancementMatchStatus.MatchedExisting),
            CountStatus(candidates.Select(value => value.MatchStatus), HomecomingEnhancementMatchStatus.NewFromHomecoming),
            currentOnly.Count,
            CountStatus(candidates.Select(value => value.MatchStatus), HomecomingEnhancementMatchStatus.Ambiguous));

    private static IReadOnlyList<HomecomingEnhancementCodeCount> CountCodes(
        IEnumerable<string?> codes) =>
        codes.GroupBy(value => value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new HomecomingEnhancementCodeCount(group.Key, group.Count()))
            .ToArray();

    private static int CountStatus(
        IEnumerable<string> statuses,
        HomecomingEnhancementMatchStatus status) =>
        statuses.Count(value => value == status.ToString());

    private static int ValidateCurrentSetCatalog(
        IReadOnlyList<CurrentEnhancementSetIdentity> currentSets) =>
        ValidateCurrentIds(currentSets.Select(value => value.AppOwnedId), "SET");

    private static int ValidateCurrentEnhancementCatalog(
        IReadOnlyList<CurrentEnhancementIdentity> currentEnhancements) =>
        ValidateCurrentIds(currentEnhancements.Select(value => value.AppOwnedId), "ENH");

    private static int ValidateCurrentIds(IEnumerable<string> ids, string prefix)
    {
        var maximum = 0;
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!unique.Add(id))
            {
                throw new HomecomingEnhancementCandidateException(
                    $"Current catalog ID '{id}' is duplicated.");
            }

            maximum = Math.Max(maximum, ParseAppOwnedId(id, prefix));
        }

        return maximum;
    }

    private static int ParseAppOwnedId(string value, string prefix)
    {
        var expectedPrefix = prefix + "-";
        if (value.Length != expectedPrefix.Length + 5
            || !value.StartsWith(expectedPrefix, StringComparison.Ordinal)
            || !int.TryParse(
                value.AsSpan(expectedPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed)
            || parsed is < 1 or > 99999)
        {
            throw new HomecomingEnhancementCandidateException(
                $"Current catalog ID '{value}' is not a valid {prefix} identifier.");
        }

        return parsed;
    }

    private static int NextId(int current, string prefix)
    {
        var next = checked(current + 1);
        if (next > 99999)
        {
            throw new HomecomingEnhancementCandidateException(
                $"No {prefix}- identifier remains available for Homecoming candidates.");
        }

        return next;
    }

    private static string ResolveRequiredMessage(
        HomecomingMessageStore messageStore,
        string key,
        string field)
    {
        if (!messageStore.TryResolve(key, out var value))
        {
            throw new HomecomingEnhancementCandidateException(
                $"{field} message key '{key}' is unresolved.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new HomecomingEnhancementCandidateException(
                $"{field} message key '{key}' resolved to an empty value.");
        }

        return value;
    }

    private static string NormalizeSetConversionCode(string code) =>
        string.Equals(code, BoostSetsPvpRarityCode, StringComparison.Ordinal)
            ? CanonicalPvpRarityCode
            : code;

    private static string? ResolveOptionalMessage(HomecomingMessageStore messageStore, string? key)
    {
        if (string.IsNullOrEmpty(key) || !messageStore.TryResolve(key, out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value;
    }

    private static IReadOnlyList<string> ReadSourceVariantIds(JsonElement item)
    {
        if (!item.TryGetProperty("sourceVariants", out var variants)
            || variants.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return variants
            .EnumerateArray()
            .Select(variant =>
            {
                if (!variant.TryGetProperty("homecomingSourceId", out var sourceIdProperty)
                    || sourceIdProperty.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(sourceIdProperty.GetString()))
                {
                    throw new HomecomingEnhancementCandidateException(
                        "Current catalog sourceVariants[].homecomingSourceId is missing.");
                }

                return sourceIdProperty.GetString()!;
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static Dictionary<NonSetEnhancementStructuralIdentity, CurrentEnhancementIdentity[]> BuildCurrentNonSetStructuralIndex(
        IReadOnlyList<CurrentEnhancementIdentity> currentEnhancements,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> discoveryBoosts)
    {
        var index = new Dictionary<NonSetEnhancementStructuralIdentity, List<CurrentEnhancementIdentity>>();
        foreach (var current in currentEnhancements.Where(value => value.EnhancementSetId is null))
        {
            if (current.SourceVariantIds.Count == 0
                || !current.SourceVariantIds.All(sourceId => discoveryBoosts.ContainsKey(sourceId)))
            {
                continue;
            }

            var identity = HomecomingNonSetEnhancementIdentitySupport.ResolveLogicalGroupIdentity(
                current.SourceVariantIds,
                discoveryBoosts);
            if (!index.TryGetValue(identity, out var matches))
            {
                matches = [];
                index[identity] = matches;
            }

            matches.Add(current);
        }

        return index.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .OrderBy(value => value.AppOwnedId, StringComparer.Ordinal)
                .ToArray());
    }

    private static string RequireString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new HomecomingEnhancementCandidateException(
                $"Current catalog field '{propertyName}' is missing.");
        }

        return property.GetString()!;
    }

    private static string? OptionalString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new HomecomingEnhancementCandidateException(
                $"Current catalog field '{propertyName}' is invalid.");
        }

        return property.GetString();
    }

    private sealed record ResolvedConcreteBoost(
        string HomecomingSourceId,
        string DisplayNameMessageKey,
        string DisplayName,
        string SourceForm);

    private sealed record ResolvedBoostSet(
        HomecomingBoostSetRecord Source,
        string DisplayName,
        string? RarityCode,
        string? RarityDisplayText,
        string CategoryCode,
        string? CategoryDisplayText,
        string CategoryCodeSource);

    private sealed record ClassifiedBoostSet(
        HomecomingBoostSetRecord Source,
        string? RarityCode,
        string? RarityDisplayText,
        string? CategoryCode);

    private sealed record LogicalEnhancement(
        string DisplayName,
        string? HomecomingSetId,
        string? EnhancementSetAppOwnedId,
        IReadOnlyList<ResolvedConcreteBoost> SourceVariants);

    private sealed record SetMatchResult(
        IReadOnlyList<HomecomingEnhancementSetCandidateRecord> Candidates,
        IReadOnlyList<HomecomingCurrentCatalogIdentityRecord> CurrentOnly);

    private sealed record EnhancementMatchResult(
        IReadOnlyList<HomecomingEnhancementCandidateRecord> Candidates,
        IReadOnlyList<HomecomingCurrentEnhancementRecord> CurrentOnly);
}

internal static class HomecomingEnhancementCandidateWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static string GetOutputPath(string salvageCandidatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(salvageCandidatePath);
        var fullPath = Path.GetFullPath(salvageCandidatePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new HomecomingEnhancementCandidateException(
                $"Candidate output path '{fullPath}' has no parent directory.");
        var extension = Path.GetExtension(fullPath);
        if (extension.Length == 0)
        {
            extension = ".json";
        }

        return Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(fullPath) + ".enhancements" + extension);
    }

    internal static byte[] Serialize(HomecomingEnhancementCandidateDocument candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(candidate, JsonOptions) + "\n");
    }

    internal static void Write(string outputPath, HomecomingEnhancementCandidateDocument candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new HomecomingEnhancementCandidateException(
                $"Candidate output path '{fullPath}' has no parent directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(fullPath, Serialize(candidate));
    }
}

internal sealed record HomecomingCurrentEnhancementCatalog(
    IReadOnlyList<CurrentEnhancementIdentity> Enhancements,
    IReadOnlyList<CurrentEnhancementSetIdentity> EnhancementSets);

internal sealed record CurrentEnhancementIdentity(
    string AppOwnedId,
    string DisplayName,
    string? EnhancementSetId,
    IReadOnlyList<string> SourceVariantIds);

internal sealed record CurrentEnhancementSetIdentity(string AppOwnedId, string DisplayName);

internal sealed record HomecomingEnhancementCandidateDocument(
    string SchemaVersion,
    HomecomingEnhancementCandidateSource Source,
    HomecomingEnhancementSetCandidateSummary EnhancementSetSummary,
    HomecomingEnhancementCandidateSummary EnhancementSummary,
    IReadOnlyList<HomecomingEnhancementSetCandidateRecord> EnhancementSets,
    IReadOnlyList<HomecomingEnhancementCandidateRecord> Enhancements,
    IReadOnlyList<HomecomingCurrentCatalogIdentityRecord> CurrentCatalogEnhancementSetsNotMatched,
    IReadOnlyList<HomecomingCurrentEnhancementRecord> CurrentCatalogEnhancementsNotMatched);

internal sealed record HomecomingEnhancementCandidateSource(
    string BuildVersion,
    string PackageRevision,
    string PowersArchive,
    string PowersMember,
    string BoostSetsArchive,
    string BoostSetsMember);

internal sealed record HomecomingEnhancementSetCandidateSummary(
    int HomecomingSets,
    int ResolvedNames,
    int MatchedExisting,
    int NewFromHomecoming,
    int CurrentCatalogNotMatched,
    int Ambiguous,
    IReadOnlyList<HomecomingEnhancementCodeCount> RarityCodeCounts,
    IReadOnlyList<HomecomingEnhancementCodeCount> CategoryCodeCounts);

internal sealed record HomecomingEnhancementCandidateSummary(
    int ConcreteBoostRecords,
    int ResolvedConcreteBoostNames,
    int LogicalEnhancements,
    int MultiVariantLogicalEnhancements,
    int MatchedExisting,
    int NewFromHomecoming,
    int CurrentCatalogNotMatched,
    int Ambiguous);

internal sealed record HomecomingEnhancementCodeCount(string? Code, int Count);

internal sealed record HomecomingEnhancementSetCandidateRecord(
    string? AppOwnedId,
    string HomecomingSetId,
    string DisplayNameMessageKey,
    string DisplayName,
    string? RarityCode,
    string? RarityDisplayText,
    string CategoryCode,
    string? CategoryDisplayText,
    string CategoryCodeSource,
    uint MinimumLevel,
    uint MaximumLevel,
    string MatchStatus,
    IReadOnlyList<string> MatchingExistingAppOwnedIds);

internal sealed record HomecomingEnhancementCandidateRecord(
    string? AppOwnedId,
    string DisplayName,
    string? HomecomingEnhancementSetId,
    string? EnhancementSetAppOwnedId,
    string MatchStatus,
    IReadOnlyList<string> MatchingExistingAppOwnedIds,
    IReadOnlyList<HomecomingEnhancementSourceVariant> SourceVariants);

internal sealed record HomecomingEnhancementSourceVariant(
    string HomecomingSourceId,
    string DisplayNameMessageKey,
    string SourceForm);

internal sealed record HomecomingCurrentCatalogIdentityRecord(
    string AppOwnedId,
    string DisplayName);

internal sealed record HomecomingCurrentEnhancementRecord(
    string AppOwnedId,
    string DisplayName,
    string? EnhancementSetId);

internal enum HomecomingEnhancementMatchStatus
{
    MatchedExisting,
    NewFromHomecoming,
    Ambiguous
}

internal enum HomecomingSetConversionCodeKind
{
    Rarity,
    Category
}

internal sealed class HomecomingEnhancementCandidateException : Exception
{
    internal HomecomingEnhancementCandidateException(string message)
        : base(message)
    {
    }
}

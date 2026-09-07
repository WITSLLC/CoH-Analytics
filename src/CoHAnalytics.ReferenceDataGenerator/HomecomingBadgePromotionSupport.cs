using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingBadgePromotionSupport
{
    internal static HomecomingBadgePromotionArtifacts Build(
        IReadOnlyList<HomecomingBadgeCandidateRecord> candidates,
        BadgeResearchPackage research)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(research);

        var crosswalkByInternal = research.TitleCrosswalk
            .Where(row => !string.IsNullOrWhiteSpace(row.InternalName) && row.InternalName != "-")
            .GroupBy(row => row.InternalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var masterByInternal = research.MasterCatalog
            .GroupBy(row => row.InternalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var masterBySetTitle = research.MasterCatalog
            .ToDictionary(row => row.SetTitleId);
        var accoladeDetails = research.AccoladeDetails;
        var zoneSlugByDisplay = research.Zones
            .ToDictionary(zone => zone.DisplayName, zone => CreateZoneId(zone.DisplayName), StringComparer.Ordinal);

        var promotedCandidates = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.AppOwnedId))
            .OrderBy(candidate => candidate.AppOwnedId, StringComparer.Ordinal)
            .ToArray();

        var badgeDocuments = new List<BadgeReferenceRecordDocument>();
        var itemDocuments = new List<ItemReferenceRecordDocument>();
        var displayNameToBadgeId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in promotedCandidates)
        {
            crosswalkByInternal.TryGetValue(candidate.HomecomingSourceId, out var crosswalk);
            masterByInternal.TryGetValue(candidate.HomecomingSourceId, out var master);
            if (master is null && crosswalk is not null)
            {
                masterBySetTitle.TryGetValue(crosswalk.SetTitleId, out master);
            }

            var referenceKind = ResolveReferenceKind(
                master?.Category,
                candidate.CanonicalCategory,
                candidate.HomecomingSourceId);
            var canonicalCategory = master?.Category ?? candidate.CanonicalCategory;
            if (IsMissionArchitectTourismBadge(candidate.HomecomingSourceId))
            {
                canonicalCategory = "Architect Entertainment";
                referenceKind = ReferenceBadgeKind.ArchitectEntertainment;
            }

            var verification = ResolveVerification(master?.Verification);
            var setTitleId = crosswalk?.SetTitleId ?? master?.SetTitleId;
            var displayName = ChooseDisplayName(candidate.HeroName, candidate.VillainName);
            var icon = NormalizeIcon(candidate.HeroIcon ?? candidate.VillainIcon);

            badgeDocuments.Add(new BadgeReferenceRecordDocument
            {
                CatalogItemId = candidate.AppOwnedId,
                HomecomingSourceId = candidate.HomecomingSourceId,
                SetTitleId = setTitleId,
                CanonicalCategory = canonicalCategory,
                BadgeType = candidate.BadgeType,
                ReferenceKind = referenceKind.ToString(),
                HeroName = candidate.HeroName,
                VillainName = candidate.VillainName,
                HeroDescription = candidate.HeroDescription,
                VillainDescription = candidate.VillainDescription,
                HeroIcon = candidate.HeroIcon,
                VillainIcon = candidate.VillainIcon,
                VerificationStatus = verification,
                RequirementText = BadgeRewardTextSupport.NormalizeRequirementText(master?.Requirement),
                RequirementLogicStatus = MapRequirementLogicStatus(verification).ToString(),
                RequirementLogicPattern = ResolveRequirementPattern(candidate.HomecomingSourceId, setTitleId, accoladeDetails)
            });

            itemDocuments.Add(new ItemReferenceRecordDocument
            {
                CatalogItemId = candidate.AppOwnedId,
                Family = nameof(ReferenceItemFamily.Badge),
                Subtype = canonicalCategory,
                CurrentDisplayName = displayName,
                ActiveStatus = nameof(ReferenceActiveStatus.Active),
                VerificationStatus = verification,
                Icon = icon
            });

            RegisterDisplayNames(displayNameToBadgeId, candidate.AppOwnedId!, displayName, candidate.HeroName, candidate.VillainName);
        }

        var badgeIdByDisplay = displayNameToBadgeId;
        var badgeIdByInternal = promotedCandidates
            .ToDictionary(candidate => candidate.HomecomingSourceId, candidate => candidate.AppOwnedId!, StringComparer.Ordinal);
        var badgeIdBySetTitle = new Dictionary<uint, string>();
        foreach (var candidate in promotedCandidates)
        {
            if (crosswalkByInternal.TryGetValue(candidate.HomecomingSourceId, out var crosswalk))
            {
                badgeIdBySetTitle[crosswalk.SetTitleId] = candidate.AppOwnedId!;
            }
        }

        var zoneDocuments = BuildZones(research, badgeIdByDisplay, zoneSlugByDisplay);
        var locationDocuments = BuildLocations(research, badgeIdByDisplay, badgeIdByInternal, zoneSlugByDisplay);
        var accoladeRequirements = BuildAccoladeRequirements(
            research,
            badgeIdByDisplay,
            badgeIdByInternal,
            badgeIdBySetTitle,
            accoladeDetails);

        ApplyCompletionBadges(badgeDocuments, research, badgeIdByDisplay, zoneSlugByDisplay);
        ApplyRewardText(badgeDocuments, research);

        return new HomecomingBadgePromotionArtifacts(
            itemDocuments,
            badgeDocuments,
            locationDocuments,
            zoneDocuments,
            accoladeRequirements,
            BuildAliases(itemDocuments, badgeDocuments, research));
    }

    internal static void RefreshAccoladeDisplayText(ItemReferenceCatalogDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var badge in document.Badges)
        {
            badge.RequirementText = BadgeRewardTextSupport.NormalizeRequirementText(badge.RequirementText);

            if (string.IsNullOrWhiteSpace(badge.RewardText))
            {
                continue;
            }

            badge.RewardText = BadgeRewardTextSupport.NormalizeAccoladeRewardPower(
                badge.RewardText,
                badge.HeroDescription,
                badge.VillainDescription);
        }
    }

    internal static void RefreshAccoladeRequirementData(
        ItemReferenceCatalogDocument document,
        BadgeResearchPackage research)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(research);

        var candidates = document.Badges
            .Where(badge => !string.IsNullOrWhiteSpace(badge.CatalogItemId)
                && !string.IsNullOrWhiteSpace(badge.HomecomingSourceId))
            .Select(CreateCandidateFromBadgeDocument)
            .ToArray();
        var artifacts = Build(candidates, research);
        var refreshedBadges = artifacts.Badges.ToDictionary(
            badge => badge.CatalogItemId!,
            StringComparer.Ordinal);

        foreach (var badge in document.Badges)
        {
            if (string.IsNullOrWhiteSpace(badge.CatalogItemId)
                || !refreshedBadges.TryGetValue(badge.CatalogItemId, out var refreshed))
            {
                continue;
            }

            badge.RequirementLogicPattern = refreshed.RequirementLogicPattern;
            badge.RequirementLogicStatus = refreshed.RequirementLogicStatus;
        }

        document.BadgeAccoladeRequirements = artifacts.BadgeAccoladeRequirements
            .OrderBy(requirement => requirement.AccoladeBadgeId, StringComparer.Ordinal)
            .ThenBy(requirement => requirement.PrerequisiteIndex ?? 0)
            .ThenBy(requirement => requirement.PrerequisiteBadgeId, StringComparer.Ordinal)
            .ToList();
    }

    private static HomecomingBadgeCandidateRecord CreateCandidateFromBadgeDocument(
        BadgeReferenceRecordDocument badge) =>
        new(
            badge.CatalogItemId,
            badge.HomecomingSourceId!,
            badge.SetTitleId ?? 0,
            badge.BadgeType ?? 0,
            "catalog-refresh",
            badge.CanonicalCategory ?? nameof(ReferenceBadgeKind.Other),
            "catalog",
            badge.HeroName ?? string.Empty,
            "catalog",
            badge.VillainName ?? badge.HeroName ?? string.Empty,
            null,
            badge.HeroDescription,
            null,
            badge.VillainDescription,
            badge.HeroIcon,
            badge.VillainIcon,
            nameof(HomecomingBadgeMatchStatus.MatchedExisting),
            []);

    internal static string CreateZoneId(string displayName)
    {
        var normalized = Regex.Replace(displayName.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return $"zone-{normalized}";
    }

    private static void ApplyRewardText(
        List<BadgeReferenceRecordDocument> badges,
        BadgeResearchPackage research)
    {
        var explorationCompletionNames = BadgeRewardTextSupport.BuildExplorationZoneCompletionDisplayNames(
            research.ExplorationZoneSets.Select(zone => zone.CompletionBadges));

        foreach (var badge in badges)
        {
            if (string.IsNullOrWhiteSpace(badge.HomecomingSourceId)
                || string.IsNullOrWhiteSpace(badge.HeroName))
            {
                continue;
            }

            research.AccoladeDetails.TryGetValue(badge.SetTitleId ?? 0, out var detail);
            badge.RewardText = BadgeRewardTextSupport.ResolveRewardText(
                badge.HomecomingSourceId,
                badge.HeroName,
                badge.VillainName,
                detail?.RewardPower,
                explorationCompletionNames,
                badge.HeroDescription,
                badge.VillainDescription);
        }
    }

    private static void ApplyCompletionBadges(
        List<BadgeReferenceRecordDocument> badges,
        BadgeResearchPackage research,
        IReadOnlyDictionary<string, string> badgeIdByDisplay,
        IReadOnlyDictionary<string, string> zoneSlugByDisplay)
    {
        var memberExplorationZones = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var location in research.ExplorationLocations)
        {
            if (!memberExplorationZones.TryGetValue(location.BadgeDisplayName, out var zones))
            {
                zones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                memberExplorationZones[location.BadgeDisplayName] = zones;
            }

            zones.Add(location.ZoneName);
        }

        var completionByMember = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var zoneSet in research.ExplorationZoneSets)
        {
            foreach (var completion in SplitNames(zoneSet.CompletionBadges))
            {
                foreach (var member in research.ExplorationLocations
                             .Where(location => location.ZoneName.Equals(zoneSet.ZoneName, StringComparison.Ordinal))
                             .Select(location => location.BadgeDisplayName))
                {
                    if (!memberExplorationZones.TryGetValue(member, out var zones) || zones.Count != 1)
                    {
                        continue;
                    }

                    completionByMember.TryAdd(member, completion);
                }
            }
        }

        foreach (var badge in badges)
        {
            var display = ChooseDisplayName(badge.HeroName!, badge.VillainName!);
            if (completionByMember.TryGetValue(display, out var completionName)
                && badgeIdByDisplay.TryGetValue(completionName, out var completionId))
            {
                badge.CompletionBadgeId = completionId;
                badge.IsZoneCompletionBadge = false;
            }

            var zoneMatch = research.Zones.FirstOrDefault(zone =>
                zone.ExplorationCompletionBadges.Any(name =>
                    name.Equals(display, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(badge.HeroName, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(badge.VillainName, StringComparison.OrdinalIgnoreCase))
                || zone.HistoryCompletionBadges.Any(name =>
                    name.Equals(display, StringComparison.OrdinalIgnoreCase)));
            if (zoneMatch is not null && zoneSlugByDisplay.TryGetValue(zoneMatch.DisplayName, out var zoneId))
            {
                badge.ZoneId ??= zoneId;
                badge.IsZoneCompletionBadge =
                    zoneMatch.ExplorationCompletionBadges.Any(name => name.Equals(display, StringComparison.OrdinalIgnoreCase))
                    || zoneMatch.HistoryCompletionBadges.Any(name => name.Equals(display, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    private static List<ZoneReferenceRecordDocument> BuildZones(
        BadgeResearchPackage research,
        IReadOnlyDictionary<string, string> badgeIdByDisplay,
        IReadOnlyDictionary<string, string> zoneSlugByDisplay)
    {
        return research.Zones
            .Select(zone => new ZoneReferenceRecordDocument
            {
                ZoneId = zoneSlugByDisplay[zone.DisplayName],
                DisplayName = zone.DisplayName,
                AlignmentNotes = zone.AlignmentNotes,
                LevelRange = zone.LevelRange,
                ZoneType = zone.ZoneType,
                ExplorationCompletionBadgeIds = ResolveBadgeIds(zone.ExplorationCompletionBadges, badgeIdByDisplay),
                HistoryCompletionBadgeIds = ResolveBadgeIds(zone.HistoryCompletionBadges, badgeIdByDisplay)
            })
            .OrderBy(zone => zone.ZoneId, StringComparer.Ordinal)
            .ToList();
    }

    private static List<BadgeLocationReferenceRecordDocument> BuildLocations(
        BadgeResearchPackage research,
        IReadOnlyDictionary<string, string> badgeIdByDisplay,
        IReadOnlyDictionary<string, string> badgeIdByInternal,
        IReadOnlyDictionary<string, string> zoneSlugByDisplay)
    {
        var badgeIdByTitle = BuildBadgeTitleLookup(research, badgeIdByDisplay, badgeIdByInternal);
        var locations = new List<BadgeLocationReferenceRecordDocument>();
        var explorationIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in research.ExplorationLocations.OrderBy(value => value.ZoneName, StringComparer.Ordinal)
                     .ThenBy(value => value.BadgeDisplayName, StringComparer.Ordinal))
        {
            if (!TryResolveExplorationBadgeId(row, badgeIdByTitle, out var badgeId)
                || !zoneSlugByDisplay.TryGetValue(row.ZoneName, out var zoneId))
            {
                continue;
            }

            var key = badgeId;
            var index = explorationIndex.TryGetValue(key, out var current) ? current + 1 : 0;
            explorationIndex[key] = index;
            locations.Add(new BadgeLocationReferenceRecordDocument
            {
                BadgeCatalogItemId = badgeId,
                ZoneId = zoneId,
                CoordinateX = row.CoordinateX,
                CoordinateY = row.CoordinateY,
                CoordinateZ = row.CoordinateZ,
                ThumbtackCommand = row.ThumbtackCommand,
                MarkerType = "ExplorationBadge",
                LocationRole = "ExplorationMarker",
                CoordinateSemantics = "SurveyedMarkerCenter",
                VerificationStatus = MapVerification(row.Verification),
                LocationIndex = index,
                TriggerDescription = row.NearbyLandmark
            });
        }

        var plaqueIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in research.HistoryPlaques.OrderBy(value => value.CollectionName, StringComparer.Ordinal)
                     .ThenBy(value => value.Sequence))
        {
            if (!badgeIdByDisplay.TryGetValue(row.CompletionBadge, out var badgeId)
                || !zoneSlugByDisplay.TryGetValue(row.ZoneName, out var zoneId))
            {
                continue;
            }

            var key = $"{badgeId}|{row.PlaqueName}";
            var index = plaqueIndex.TryGetValue(key, out var current) ? current + 1 : 0;
            plaqueIndex[key] = index;
            locations.Add(new BadgeLocationReferenceRecordDocument
            {
                BadgeCatalogItemId = badgeId,
                ZoneId = zoneId,
                CoordinateX = row.CoordinateX,
                CoordinateY = row.CoordinateY,
                CoordinateZ = row.CoordinateZ,
                ThumbtackCommand = row.ThumbtackCommand,
                MarkerType = "HistoryPlaque",
                LocationRole = "HistoryPlaque",
                CoordinateSemantics = "SurveyedPlaqueFaceCenter",
                VerificationStatus = MapVerification(row.Verification),
                LocationIndex = locations.Count(value => value.BadgeCatalogItemId == badgeId),
                TriggerDescription = row.NearbyLandmark
            });
        }

        return locations
            .OrderBy(location => location.BadgeCatalogItemId, StringComparer.Ordinal)
            .ThenBy(location => location.LocationIndex)
            .ToList();
    }

    private static List<BadgeAccoladeRequirementRecordDocument> BuildAccoladeRequirements(
        BadgeResearchPackage research,
        IReadOnlyDictionary<string, string> badgeIdByDisplay,
        IReadOnlyDictionary<string, string> badgeIdByInternal,
        IReadOnlyDictionary<uint, string> badgeIdBySetTitle,
        IReadOnlyDictionary<uint, BadgeResearchAccoladeDetail> accoladeDetails)
    {
        var requirements = new List<BadgeAccoladeRequirementRecordDocument>();
        var prerequisiteIdByTitle = BuildPrerequisiteLookup(research, badgeIdByDisplay, badgeIdByInternal);
        foreach (var detail in accoladeDetails.Values.OrderBy(value => value.SetTitleId))
        {
            if (!TryResolveAccoladeBadgeId(detail, badgeIdBySetTitle, badgeIdByInternal, out var accoladeId))
            {
                continue;
            }

            var prerequisiteIndex = 0;
            var seenPrerequisiteIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prerequisiteName in detail.LinkedPrerequisiteDisplayNames)
            {
                if (!TryResolvePrerequisiteBadgeId(prerequisiteName, prerequisiteIdByTitle, out var prerequisiteId))
                {
                    continue;
                }

                if (!seenPrerequisiteIds.Add(prerequisiteId))
                {
                    continue;
                }

                requirements.Add(new BadgeAccoladeRequirementRecordDocument
                {
                    AccoladeBadgeId = accoladeId,
                    PrerequisiteBadgeId = prerequisiteId,
                    PrerequisiteIndex = prerequisiteIndex,
                    LogicGroup = 0,
                    RequirementLogicStatus = MapRequirementLogicStatus(MapVerification(detail.Verification)).ToString()
                });
                prerequisiteIndex++;
            }
        }

        return requirements;
    }

    private static Dictionary<string, string> BuildPrerequisiteLookup(
        BadgeResearchPackage research,
        IReadOnlyDictionary<string, string> badgeIdByDisplay,
        IReadOnlyDictionary<string, string> badgeIdByInternal) =>
        BuildBadgeTitleLookup(research, badgeIdByDisplay, badgeIdByInternal);

    private static Dictionary<string, string> BuildBadgeTitleLookup(
        BadgeResearchPackage research,
        IReadOnlyDictionary<string, string> badgeIdByDisplay,
        IReadOnlyDictionary<string, string> badgeIdByInternal)
    {
        var lookup = new Dictionary<string, string>(badgeIdByDisplay, StringComparer.OrdinalIgnoreCase);
        foreach (var master in research.MasterCatalog)
        {
            if (badgeIdByInternal.TryGetValue(master.InternalName, out var badgeId))
            {
                RegisterTitleLookupKeys(lookup, badgeId, master.DisplayTitles);
            }
        }

        foreach (var crosswalk in research.TitleCrosswalk)
        {
            if (!badgeIdByInternal.TryGetValue(crosswalk.InternalName, out var badgeId))
            {
                continue;
            }

            RegisterTitleLookupKeys(
                lookup,
                badgeId,
                crosswalk.HeroMale,
                crosswalk.HeroFemale,
                crosswalk.VillainMale,
                crosswalk.VillainFemale,
                crosswalk.PraetorianMale,
                crosswalk.PraetorianFemale);
        }

        return lookup;
    }

    private static bool TryResolveExplorationBadgeId(
        BadgeResearchExplorationLocationRow row,
        IReadOnlyDictionary<string, string> badgeIdByTitle,
        out string badgeId)
    {
        foreach (var candidate in EnumerateExplorationTitleCandidates(row))
        {
            if (badgeIdByTitle.TryGetValue(NormalizeBadgeTitle(candidate), out badgeId!))
            {
                return true;
            }
        }

        badgeId = string.Empty;
        return false;
    }

    private static IEnumerable<string> EnumerateExplorationTitleCandidates(BadgeResearchExplorationLocationRow row)
    {
        yield return row.BadgeDisplayName;
        foreach (var part in SplitNames(row.BadgeDisplayName))
        {
            yield return part;
        }

        const string aliasMarker = "aliases/variants:";
        var markerIndex = row.AlignmentRestrictions.IndexOf(aliasMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            yield break;
        }

        var aliases = row.AlignmentRestrictions[(markerIndex + aliasMarker.Length)..].Trim();
        yield return aliases;
        foreach (var part in SplitNames(aliases))
        {
            yield return part;
        }
    }

    private static void RegisterTitleLookupKeys(
        IDictionary<string, string> lookup,
        string badgeId,
        params string?[] titles)
    {
        foreach (var title in titles.Where(value => !string.IsNullOrWhiteSpace(value) && value != "-"))
        {
            lookup[NormalizeBadgeTitle(title!)] = badgeId;
            var primary = ChoosePrimaryTitle(title!);
            lookup[NormalizeBadgeTitle(primary)] = badgeId;
            foreach (var part in SplitNames(primary))
            {
                lookup[NormalizeBadgeTitle(part)] = badgeId;
            }

            foreach (var part in SplitNames(title!))
            {
                lookup[NormalizeBadgeTitle(part)] = badgeId;
            }
        }
    }

    private static bool TryResolveAccoladeBadgeId(
        BadgeResearchAccoladeDetail detail,
        IReadOnlyDictionary<uint, string> badgeIdBySetTitle,
        IReadOnlyDictionary<string, string> badgeIdByInternal,
        out string badgeId)
    {
        if (badgeIdBySetTitle.TryGetValue(detail.SetTitleId, out badgeId!))
        {
            return true;
        }

        return badgeIdByInternal.TryGetValue(detail.InternalName, out badgeId!);
    }

    private static bool TryResolvePrerequisiteBadgeId(
        string prerequisiteName,
        IReadOnlyDictionary<string, string> prerequisiteIdByTitle,
        out string badgeId)
    {
        var normalized = NormalizeBadgeTitle(prerequisiteName);
        if (prerequisiteIdByTitle.TryGetValue(normalized, out badgeId!))
        {
            return true;
        }

        return prerequisiteIdByTitle.TryGetValue(prerequisiteName.Trim(), out badgeId!);
    }

    private static List<ItemReferenceAliasRecordDocument> BuildAliases(
        IReadOnlyList<ItemReferenceRecordDocument> items,
        IReadOnlyList<BadgeReferenceRecordDocument> badges,
        BadgeResearchPackage research)
    {
        var crosswalkByInternal = research.TitleCrosswalk
            .Where(row => !string.IsNullOrWhiteSpace(row.InternalName) && row.InternalName != "-")
            .GroupBy(row => row.InternalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var masterByInternal = research.MasterCatalog
            .GroupBy(row => row.InternalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var badgesById = badges
            .Where(badge => !string.IsNullOrWhiteSpace(badge.CatalogItemId))
            .ToDictionary(badge => badge.CatalogItemId!, StringComparer.Ordinal);

        var receiptOwners = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var candidatesByBadge = new Dictionary<string, List<(string Text, bool IsPreferred)>>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item.CatalogItemId is null || !badgesById.TryGetValue(item.CatalogItemId, out var badge))
            {
                continue;
            }

            var candidates = new List<(string Text, bool IsPreferred)>();
            CollectLogReceiptCandidates(
                candidates,
                item,
                badge,
                crosswalkByInternal,
                masterByInternal);
            candidatesByBadge[item.CatalogItemId] = candidates;

            foreach (var (text, _) in candidates)
            {
                var lookupKey = ItemReferenceLookup.NormalizeLookupKey(text);
                if (string.IsNullOrEmpty(lookupKey))
                {
                    continue;
                }

                if (!receiptOwners.TryGetValue(lookupKey, out var owners))
                {
                    owners = new HashSet<string>(StringComparer.Ordinal);
                    receiptOwners[lookupKey] = owners;
                }

                owners.Add(item.CatalogItemId);
            }
        }

        var aliases = new List<ItemReferenceAliasRecordDocument>();
        foreach (var (badgeId, candidates) in candidatesByBadge)
        {
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (text, isPreferred) in candidates)
            {
                var lookupKey = ItemReferenceLookup.NormalizeLookupKey(text);
                if (string.IsNullOrEmpty(lookupKey)
                    || seenKeys.Contains(lookupKey)
                    || !receiptOwners.TryGetValue(lookupKey, out var owners)
                    || owners.Count != 1
                    || !owners.Contains(badgeId))
                {
                    continue;
                }

                seenKeys.Add(lookupKey);
                aliases.Add(new ItemReferenceAliasRecordDocument
                {
                    CatalogItemId = badgeId,
                    Locale = "en",
                    Text = text,
                    NameKind = nameof(ReferenceAliasNameKind.LogReceipt),
                    IsPreferred = isPreferred
                });
            }
        }

        return aliases
            .OrderBy(alias => alias.CatalogItemId, StringComparer.Ordinal)
            .ThenBy(alias => alias.Text, StringComparer.Ordinal)
            .ToList();
    }

    internal static BadgeLogReceiptAliasAuditResult AuditLogReceiptAliasCoverage(
        IReadOnlyList<ItemReferenceRecordDocument> items,
        IReadOnlyList<BadgeReferenceRecordDocument> badges,
        BadgeResearchPackage research)
    {
        var crosswalkByInternal = research.TitleCrosswalk
            .Where(row => !string.IsNullOrWhiteSpace(row.InternalName) && row.InternalName != "-")
            .GroupBy(row => row.InternalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var masterByInternal = research.MasterCatalog
            .GroupBy(row => row.InternalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var badgesById = badges
            .Where(badge => !string.IsNullOrWhiteSpace(badge.CatalogItemId))
            .ToDictionary(badge => badge.CatalogItemId!, StringComparer.Ordinal);
        var builtAliases = BuildAliases(items, badges, research);
        var aliasKeysByBadge = builtAliases
            .GroupBy(alias => alias.CatalogItemId ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(alias => ItemReferenceLookup.NormalizeLookupKey(alias.Text ?? string.Empty))
                    .Where(key => !string.IsNullOrEmpty(key))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.Ordinal);

        var receiptOwners = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var missingSafeReceiptAliases = new List<BadgeLogReceiptAliasGap>();
        var ambiguousReceiptNames = new List<BadgeLogReceiptAliasCollision>();

        foreach (var item in items)
        {
            if (item.CatalogItemId is null || !badgesById.TryGetValue(item.CatalogItemId, out var badge))
            {
                continue;
            }

            var candidates = new List<(string Text, bool IsPreferred)>();
            CollectLogReceiptCandidates(
                candidates,
                item,
                badge,
                crosswalkByInternal,
                masterByInternal);

            foreach (var (text, _) in candidates)
            {
                var lookupKey = ItemReferenceLookup.NormalizeLookupKey(text);
                if (string.IsNullOrEmpty(lookupKey))
                {
                    continue;
                }

                if (!receiptOwners.TryGetValue(lookupKey, out var owners))
                {
                    owners = new HashSet<string>(StringComparer.Ordinal);
                    receiptOwners[lookupKey] = owners;
                }

                owners.Add(item.CatalogItemId);
            }
        }

        foreach (var collision in receiptOwners
                     .Where(pair => pair.Value.Count > 1)
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            ambiguousReceiptNames.Add(new BadgeLogReceiptAliasCollision(
                collision.Key,
                collision.Value.OrderBy(id => id, StringComparer.Ordinal).ToArray()));
        }

        var alreadyResolvable = 0;
        foreach (var item in items)
        {
            if (item.CatalogItemId is null || !badgesById.TryGetValue(item.CatalogItemId, out var badge))
            {
                continue;
            }

            var preferredReceipt = ChoosePreferredLogReceiptName(item, badge);
            if (preferredReceipt is null)
            {
                continue;
            }

            var lookupKey = ItemReferenceLookup.NormalizeLookupKey(preferredReceipt);
            var owners = receiptOwners.GetValueOrDefault(lookupKey);
            var aliasKeys = aliasKeysByBadge.GetValueOrDefault(item.CatalogItemId);
            if (owners?.Count == 1
                && owners.Contains(item.CatalogItemId)
                && aliasKeys is not null
                && aliasKeys.Contains(lookupKey))
            {
                alreadyResolvable++;
                continue;
            }

            if (owners?.Count > 1)
            {
                continue;
            }

            missingSafeReceiptAliases.Add(new BadgeLogReceiptAliasGap(
                item.CatalogItemId,
                preferredReceipt,
                lookupKey));
        }

        return new BadgeLogReceiptAliasAuditResult(
            items.Count(item => item.CatalogItemId is not null && badgesById.ContainsKey(item.CatalogItemId)),
            alreadyResolvable,
            missingSafeReceiptAliases,
            builtAliases.Count,
            ambiguousReceiptNames);
    }

    private static void CollectLogReceiptCandidates(
        List<(string Text, bool IsPreferred)> candidates,
        ItemReferenceRecordDocument item,
        BadgeReferenceRecordDocument badge,
        IReadOnlyDictionary<string, BadgeResearchTitleCrosswalkRow> crosswalkByInternal,
        IReadOnlyDictionary<string, BadgeResearchMasterCatalogRow> masterByInternal)
    {
        var preferredReceipt = ChoosePreferredLogReceiptName(item, badge);
        void Add(string? text, bool isPreferred = false)
        {
            if (!IsLogReceiptSafeName(text))
            {
                return;
            }

            var trimmed = text!.Trim();
            var lookupKey = ItemReferenceLookup.NormalizeLookupKey(trimmed);
            if (string.IsNullOrEmpty(lookupKey)
                || candidates.Any(candidate =>
                    string.Equals(
                        ItemReferenceLookup.NormalizeLookupKey(candidate.Text),
                        lookupKey,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            candidates.Add((trimmed, isPreferred));
        }

        Add(item.CurrentDisplayName, string.Equals(preferredReceipt, item.CurrentDisplayName, StringComparison.OrdinalIgnoreCase));
        Add(badge.HeroName, string.Equals(preferredReceipt, badge.HeroName, StringComparison.OrdinalIgnoreCase));
        Add(badge.VillainName, string.Equals(preferredReceipt, badge.VillainName, StringComparison.OrdinalIgnoreCase));

        foreach (var variant in ExpandLogReceiptNameVariants(badge.HeroName))
        {
            Add(variant);
        }

        foreach (var variant in ExpandLogReceiptNameVariants(badge.VillainName))
        {
            Add(variant);
        }

        foreach (var part in SplitNames(item.CurrentDisplayName ?? string.Empty))
        {
            Add(part);
        }

        if (!string.IsNullOrWhiteSpace(badge.HomecomingSourceId))
        {
            if (crosswalkByInternal.TryGetValue(badge.HomecomingSourceId, out var crosswalk))
            {
                Add(crosswalk.HeroMale);
                Add(crosswalk.HeroFemale);
                Add(crosswalk.VillainMale);
                Add(crosswalk.VillainFemale);
                Add(crosswalk.PraetorianMale);
                Add(crosswalk.PraetorianFemale);
            }

            if (masterByInternal.TryGetValue(badge.HomecomingSourceId, out var master))
            {
                foreach (var title in SplitNames(master.DisplayTitles))
                {
                    var primary = ChoosePrimaryTitle(title);
                    Add(primary);
                    foreach (var part in SplitNames(primary))
                    {
                        Add(part);
                    }
                }
            }
        }
    }

    private static string? ChoosePreferredLogReceiptName(
        ItemReferenceRecordDocument item,
        BadgeReferenceRecordDocument badge)
    {
        if (IsLogReceiptSafeName(item.CurrentDisplayName)
            && !item.CurrentDisplayName!.Contains('/'))
        {
            return NormalizeBadgeTitle(item.CurrentDisplayName);
        }

        if (IsLogReceiptSafeName(badge.HeroName))
        {
            return NormalizeBadgeTitle(badge.HeroName!);
        }

        if (string.Equals(badge.HeroName, badge.VillainName, StringComparison.Ordinal)
            && IsLogReceiptSafeName(badge.VillainName))
        {
            return NormalizeBadgeTitle(badge.VillainName!);
        }

        return null;
    }

    private static bool IsLogReceiptSafeName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim() != "-"
        && !value.Contains('{')
        && !value.Contains('}');

    private static IEnumerable<string> ExpandLogReceiptNameVariants(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        if (!value.Contains('{'))
        {
            yield return value.Trim();
            yield break;
        }

        var match = Regex.Match(
            value,
            @"\{Hero\.gender=male\s+(.+?)\|(.+?)\}(.*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            yield break;
        }

        var male = match.Groups[1].Value.Trim();
        var female = match.Groups[2].Value.Trim();
        var suffix = match.Groups[3].Value;
        yield return $"{male}{suffix}".Trim();
        yield return $"{female}{suffix}".Trim();
    }

    private static void RegisterDisplayNames(
        IDictionary<string, string> map,
        string badgeId,
        params string?[] names)
    {
        foreach (var name in names.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var normalized = NormalizeBadgeTitle(name!);
            map.TryAdd(normalized, badgeId);
            foreach (var alias in SplitNames(name!))
            {
                map.TryAdd(NormalizeBadgeTitle(alias), badgeId);
            }
        }
    }

    private static List<string> ResolveBadgeIds(
        IReadOnlyList<string> names,
        IReadOnlyDictionary<string, string> badgeIdByDisplay) =>
        names
            .Select(NormalizeBadgeTitle)
            .Select(name => badgeIdByDisplay.TryGetValue(name, out var id) ? id : null)
            .Where(id => id is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

    private static bool IsMissionArchitectTourismBadge(string? homecomingSourceId) =>
        string.Equals(homecomingSourceId, "MissionArchitectTourism", StringComparison.Ordinal);

    private static ReferenceBadgeKind ResolveReferenceKind(
        string? researchCategory,
        string homecomingCategory,
        string? homecomingSourceId)
    {
        if (IsMissionArchitectTourismBadge(homecomingSourceId))
        {
            return ReferenceBadgeKind.ArchitectEntertainment;
        }

        var category = researchCategory ?? homecomingCategory;
        return category switch
        {
            "Exploration" => ReferenceBadgeKind.ExplorationBadge,
            "History" => ReferenceBadgeKind.HistoryPlaque,
            "Accolades" => ReferenceBadgeKind.Accolade,
            "Achievement" => ReferenceBadgeKind.Achievement,
            "Accomplishment" => ReferenceBadgeKind.Accomplishment,
            "Architect Entertainment" => ReferenceBadgeKind.ArchitectEntertainment,
            "Consignment" => ReferenceBadgeKind.Consignment,
            "Day Jobs" => ReferenceBadgeKind.DayJobs,
            "Defeats" => ReferenceBadgeKind.Defeats,
            "Events" => ReferenceBadgeKind.Events,
            "Gladiator" => ReferenceBadgeKind.Gladiator,
            "Invention" => ReferenceBadgeKind.Invention,
            "Ouroboros" => ReferenceBadgeKind.Ouroboros,
            "PVP" => ReferenceBadgeKind.Pvp,
            "Veteran" => ReferenceBadgeKind.Veteran,
            "TOURISM" => ReferenceBadgeKind.ExplorationBadge,
            "HISTORY" => ReferenceBadgeKind.HistoryPlaque,
            _ => ReferenceBadgeKind.Other
        };
    }

    private static string ResolveVerification(string? verification) =>
        string.Equals(verification, "Verified", StringComparison.OrdinalIgnoreCase)
            ? nameof(ReferenceVerificationStatus.VerifiedMultiSource)
            : nameof(ReferenceVerificationStatus.SecondaryOnly);

    private static string MapVerification(string verification) =>
        verification.Contains("Verified", StringComparison.OrdinalIgnoreCase)
            ? nameof(ReferenceVerificationStatus.VerifiedMultiSource)
            : nameof(ReferenceVerificationStatus.SecondaryOnly);

    private static ReferenceRequirementLogicStatus MapRequirementLogicStatus(string verification) =>
        verification.Contains("Verified", StringComparison.OrdinalIgnoreCase)
            ? ReferenceRequirementLogicStatus.Verified
            : ReferenceRequirementLogicStatus.Partial;

    private static string? ResolveRequirementPattern(
        string internalName,
        uint? setTitleId,
        IReadOnlyDictionary<uint, BadgeResearchAccoladeDetail> accoladeDetails)
    {
        if (setTitleId is not null && accoladeDetails.TryGetValue(setTitleId.Value, out var detail))
        {
            return detail.RequirementLogicPattern.ToString();
        }

        return null;
    }

    private static string ChooseDisplayName(string heroName, string villainName) =>
        !string.Equals(heroName, villainName, StringComparison.Ordinal)
            ? $"{heroName} / {villainName}"
            : heroName;

    private static string ChoosePrimaryTitle(string displayTitles)
    {
        var first = displayTitles.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            ?? displayTitles;
        var colon = first.IndexOf(':');
        if (colon >= 0)
        {
            first = first[(colon + 1)..].Trim();
        }

        return NormalizeBadgeTitle(first);
    }

    private static string NormalizeBadgeTitle(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.EndsWith(" Badge", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^6];
        }

        return trimmed;
    }

    private static string? NormalizeIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        return icon.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? icon : $"{icon}.tga";
    }

    internal static IReadOnlyList<ItemReferenceAliasRecordDocument> BuildLogReceiptAliasesFromCatalogBadges(
        ItemReferenceCatalogDocument document,
        BadgeResearchPackage research)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(research);

        var candidates = document.Badges
            .Where(badge =>
                !string.IsNullOrWhiteSpace(badge.CatalogItemId)
                && !string.IsNullOrWhiteSpace(badge.HomecomingSourceId))
            .Select(badge => new HomecomingBadgeCandidateRecord(
                badge.CatalogItemId,
                badge.HomecomingSourceId!,
                badge.SetTitleId ?? 0,
                badge.BadgeType ?? 0,
                "catalog-log-receipt-sync",
                badge.CanonicalCategory ?? nameof(ReferenceBadgeKind.Other),
                "catalog",
                badge.HeroName ?? string.Empty,
                "catalog",
                badge.VillainName ?? badge.HeroName ?? string.Empty,
                null,
                badge.HeroDescription,
                null,
                badge.VillainDescription,
                badge.HeroIcon,
                badge.VillainIcon,
                nameof(HomecomingBadgeMatchStatus.MatchedExisting),
                []))
            .ToArray();

        return Build(candidates, research).Aliases;
    }

    internal static int MergeBuiltLogReceiptAliases(
        ItemReferenceCatalogDocument document,
        IReadOnlyList<ItemReferenceAliasRecordDocument> builtAliases)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(builtAliases);

        var aliases = document.Aliases?.ToList() ?? [];
        var aliasKeys = aliases
            .Select(alias => ItemReferenceLookup.NormalizeLookupKey(alias.Text ?? string.Empty))
            .Where(key => !string.IsNullOrEmpty(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var alias in builtAliases)
        {
            if (!string.Equals(
                    alias.NameKind,
                    nameof(ReferenceAliasNameKind.LogReceipt),
                    StringComparison.Ordinal))
            {
                continue;
            }

            var lookupKey = ItemReferenceLookup.NormalizeLookupKey(alias.Text ?? string.Empty);
            if (string.IsNullOrEmpty(lookupKey) || !aliasKeys.Add(lookupKey))
            {
                continue;
            }

            aliases.Add(alias);
            added++;
        }

        document.Aliases = aliases
            .OrderBy(value => value.CatalogItemId, StringComparer.Ordinal)
            .ThenBy(value => value.Text, StringComparer.Ordinal)
            .ToList();
        return added;
    }

    private static IEnumerable<string> SplitNames(string value) =>
        value.Split(new[] { '/', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal sealed record HomecomingBadgePromotionArtifacts(
    IReadOnlyList<ItemReferenceRecordDocument> Items,
    IReadOnlyList<BadgeReferenceRecordDocument> Badges,
    IReadOnlyList<BadgeLocationReferenceRecordDocument> BadgeLocations,
    IReadOnlyList<ZoneReferenceRecordDocument> Zones,
    IReadOnlyList<BadgeAccoladeRequirementRecordDocument> BadgeAccoladeRequirements,
    IReadOnlyList<ItemReferenceAliasRecordDocument> Aliases);

internal sealed record BadgeLogReceiptAliasAuditResult(
    int BadgesAudited,
    int AlreadyResolvableByPreferredReceipt,
    IReadOnlyList<BadgeLogReceiptAliasGap> MissingSafeReceiptAliases,
    int SafeAliasesAdded,
    IReadOnlyList<BadgeLogReceiptAliasCollision> AmbiguousReceiptNames);

internal sealed record BadgeLogReceiptAliasGap(
    string CatalogItemId,
    string PreferredReceiptName,
    string NormalizedLookupKey);

internal sealed record BadgeLogReceiptAliasCollision(
    string NormalizedLookupKey,
    IReadOnlyList<string> CatalogItemIds);

internal sealed record HomecomingBadgePromotionResult(
    string CatalogPath,
    string BuildVersion,
    string CatalogSha256,
    string DeterminismSha256,
    int SourceBadgeRows,
    BadgePromotionStats Stats);

internal sealed class BadgePromotionStats
{
    public int BadgesPromoted { get; set; }

    public int BadgesAdded { get; set; }

    public int BadgesUpdated { get; set; }

    public int BadgesRemoved { get; set; }

    public int BadgeLocations { get; set; }

    public int Zones { get; set; }

    public int AccoladeRequirements { get; set; }

    public int AliasesAdded { get; set; }

    public Dictionary<string, int> ReferenceKindCounts { get; } = new(StringComparer.Ordinal);

    public int ResearchCrosswalkRows { get; set; }

    public int ResearchExplorationLocations { get; set; }

    public int ResearchPlaques { get; set; }

    public int ResearchAccolades { get; set; }
}

internal sealed class HomecomingBadgePromotionException : Exception
{
    internal HomecomingBadgePromotionException(string message)
        : base(message)
    {
    }
}

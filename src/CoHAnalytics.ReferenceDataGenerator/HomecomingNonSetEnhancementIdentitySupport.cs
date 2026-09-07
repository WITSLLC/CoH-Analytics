using System.Text;
using System.Text.RegularExpressions;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal enum NonSetEnhancementIdentityEvidenceClass
{
    CraftedSeries,
    OriginStructuralPair,
    SingleSource
}

internal readonly record struct NonSetEnhancementStructuralIdentity(
    NonSetEnhancementIdentityEvidenceClass EvidenceClass,
    ReferenceEnhancementFamily Family,
    string Key) : IComparable<NonSetEnhancementStructuralIdentity>
{
    public int CompareTo(NonSetEnhancementStructuralIdentity other)
    {
        var evidence = EvidenceClass.CompareTo(other.EvidenceClass);
        if (evidence != 0)
        {
            return evidence;
        }

        var family = Family.CompareTo(other.Family);
        return family != 0 ? family : string.Compare(Key, other.Key, StringComparison.Ordinal);
    }

    public string Describe() =>
        $"{EvidenceClass}|{Family}|{Key}";
}

internal sealed record NonSetLogicalGroupResult(
    NonSetEnhancementStructuralIdentity StructuralIdentity,
    IReadOnlyList<string> SourceIds,
    string RepresentativeDisplayName);

internal static class HomecomingNonSetEnhancementIdentitySupport
{
    internal static IReadOnlyList<NonSetLogicalGroupResult> GroupUnassignedSources(
        IReadOnlyList<string> unassignedSourceIds,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> discoveryBoosts,
        IReadOnlyDictionary<string, string> displayNamesBySourceId)
    {
        var provisional = new Dictionary<string, List<(string SourceId, HomecomingBoostDiscoveryRecord Discovery)>>(
            StringComparer.Ordinal);

        foreach (var sourceId in unassignedSourceIds.OrderBy(value => value, StringComparer.Ordinal))
        {
            if (!discoveryBoosts.TryGetValue(sourceId, out var discovery))
            {
                throw new HomecomingEnhancementCandidateException(
                    $"Boost source '{sourceId}' has no structural discovery record.");
            }

            var bucketKey = BuildProvisionalBucketKey(sourceId, discovery);
            if (!provisional.TryGetValue(bucketKey, out var members))
            {
                members = [];
                provisional[bucketKey] = members;
            }

            members.Add((sourceId, discovery));
        }

        var groups = new List<NonSetLogicalGroupResult>(provisional.Count);
        foreach (var (bucketKey, members) in provisional.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            if (bucketKey.StartsWith("OriginCandidate|", StringComparison.Ordinal) && members.Count > 2)
            {
                foreach (var member in members.OrderBy(value => value.SourceId, StringComparer.Ordinal))
                {
                    groups.Add(FinalizeSingleSourceBucket(member, displayNamesBySourceId));
                }

                continue;
            }

            groups.Add(FinalizeBucket(bucketKey, members, displayNamesBySourceId));
        }

        return groups
            .OrderBy(value => value.SourceIds[0], StringComparer.Ordinal)
            .ToArray();
    }

    internal static NonSetEnhancementStructuralIdentity ResolveLogicalGroupIdentity(
        IReadOnlyList<string> sourceIds,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> discoveryBoosts)
    {
        if (sourceIds.Count == 0)
        {
            throw new HomecomingEnhancementCandidateException(
                "Non-set logical Enhancement identity requires at least one source variant.");
        }

        var members = sourceIds
            .OrderBy(value => value, StringComparer.Ordinal)
            .Select(sourceId =>
            {
                if (!discoveryBoosts.TryGetValue(sourceId, out var discovery))
                {
                    throw new HomecomingEnhancementCandidateException(
                        $"Boost source '{sourceId}' has no structural discovery record.");
                }

                return (SourceId: sourceId, Discovery: discovery);
            })
            .ToArray();

        if (members.All(value => IsCraftedInventionSeriesId(value.SourceId)))
        {
            return ResolveCraftedSeriesIdentity(members);
        }

        if (members.Length == 2 && members.All(value => !IsCraftedInventionSeriesId(value.SourceId)))
        {
            return ResolveOriginPairIdentity(members);
        }

        if (members.Length == 1)
        {
            return ResolveSingleSourceIdentity(members[0].SourceId, members[0].Discovery);
        }

        throw new HomecomingEnhancementCandidateException(
            $"Non-set logical Enhancement sources [{string.Join(", ", sourceIds)}] " +
            "do not match a proven structural identity rule.");
    }

    internal static string BuildProvisionalBucketKey(
        string sourceId,
        HomecomingBoostDiscoveryRecord discovery)
    {
        var family = HomecomingEnhancementFamilySupport.ClassifySource(sourceId, discovery);
        if (family == ReferenceEnhancementFamily.CraftedInvention && IsCraftedInventionSeriesId(sourceId))
        {
            return "CraftedSeries|" + StripCraftedSeriesBaseId(sourceId);
        }

        if (family == ReferenceEnhancementFamily.OriginOrTraining)
        {
            return "OriginCandidate|"
                + ExtractOriginTrainingPrefix(sourceId)
                + "|"
                + BuildOriginApplicabilityKey(discovery);
        }

        return "SingleSource|" + sourceId;
    }

    internal static string ExtractOriginTrainingPrefix(string sourceId)
    {
        var match = Regex.Match(
            sourceId,
            @"^Boosts\.(?<prefix>Generic|Magic|Mutation|Natural|Science|Technology)_",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            throw new HomecomingEnhancementCandidateException(
                $"Boost source '{sourceId}' does not match a proven origin/training source prefix.");
        }

        return match.Groups["prefix"].Value;
    }

    private static NonSetLogicalGroupResult FinalizeSingleSourceBucket(
        (string SourceId, HomecomingBoostDiscoveryRecord Discovery) member,
        IReadOnlyDictionary<string, string> displayNamesBySourceId)
    {
        var identity = ResolveSingleSourceIdentity(member.SourceId, member.Discovery);
        return BuildGroupResult(identity, [member.SourceId], displayNamesBySourceId);
    }

    private static NonSetLogicalGroupResult FinalizeBucket(
        string bucketKey,
        IReadOnlyList<(string SourceId, HomecomingBoostDiscoveryRecord Discovery)> members,
        IReadOnlyDictionary<string, string> displayNamesBySourceId)
    {
        var orderedMembers = members
            .OrderBy(value => value.SourceId, StringComparer.Ordinal)
            .ToArray();
        var orderedSourceIds = orderedMembers
            .Select(value => value.SourceId)
            .ToArray();

        if (bucketKey.StartsWith("CraftedSeries|", StringComparison.Ordinal))
        {
            var identity = ResolveCraftedSeriesIdentity(orderedMembers);
            ValidateCraftedSeriesInvariants(orderedMembers, identity);
            return BuildGroupResult(identity, orderedSourceIds, displayNamesBySourceId);
        }

        if (bucketKey.StartsWith("OriginCandidate|", StringComparison.Ordinal))
        {
            if (orderedMembers.Length == 1)
            {
                var singleIdentity = ResolveSingleSourceIdentity(
                    orderedMembers[0].SourceId,
                    orderedMembers[0].Discovery);
                return BuildGroupResult(singleIdentity, orderedSourceIds, displayNamesBySourceId);
            }

            if (orderedMembers.Length == 2)
            {
                var pairIdentity = ResolveOriginPairIdentity(orderedMembers);
                ValidateOriginPairInvariants(orderedMembers, pairIdentity);
                return BuildGroupResult(pairIdentity, orderedSourceIds, displayNamesBySourceId);
            }

            throw new HomecomingEnhancementCandidateException(
                $"Origin structural applicability '{bucketKey}' matched {orderedMembers.Length} concrete Boost " +
                "sources; expected 1 or 2. This indicates a structural identity collision.");
        }

        if (orderedMembers.Length != 1)
        {
            throw new HomecomingEnhancementCandidateException(
                $"Single-source structural bucket '{bucketKey}' matched {orderedMembers.Length} concrete Boost " +
                "sources; expected 1.");
        }

        var singleSourceIdentity = ResolveSingleSourceIdentity(
            orderedMembers[0].SourceId,
            orderedMembers[0].Discovery);
        return BuildGroupResult(singleSourceIdentity, orderedSourceIds, displayNamesBySourceId);
    }

    private static NonSetLogicalGroupResult BuildGroupResult(
        NonSetEnhancementStructuralIdentity identity,
        IReadOnlyList<string> orderedSourceIds,
        IReadOnlyDictionary<string, string> displayNamesBySourceId)
    {
        if (!displayNamesBySourceId.TryGetValue(orderedSourceIds[0], out var displayName))
        {
            throw new HomecomingEnhancementCandidateException(
                $"Boost source '{orderedSourceIds[0]}' has no resolved display name.");
        }

        return new NonSetLogicalGroupResult(identity, orderedSourceIds, displayName);
    }

    private static NonSetEnhancementStructuralIdentity ResolveCraftedSeriesIdentity(
        IReadOnlyList<(string HomecomingSourceId, HomecomingBoostDiscoveryRecord Discovery)> members)
    {
        var baseNames = members
            .Select(value => StripCraftedSeriesBaseId(value.HomecomingSourceId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (baseNames.Length != 1)
        {
            throw new HomecomingEnhancementCandidateException(
                "Crafted invention series members do not share one canonical series base identity.");
        }

        var families = members
            .Select(value => HomecomingEnhancementFamilySupport.ClassifySource(
                value.HomecomingSourceId,
                value.Discovery))
            .Distinct()
            .ToArray();
        if (families.Length != 1 || families[0] != ReferenceEnhancementFamily.CraftedInvention)
        {
            throw new HomecomingEnhancementCandidateException(
                "Crafted invention series members must all classify as CraftedInvention.");
        }

        return new NonSetEnhancementStructuralIdentity(
            NonSetEnhancementIdentityEvidenceClass.CraftedSeries,
            ReferenceEnhancementFamily.CraftedInvention,
            baseNames[0]);
    }

    private static NonSetEnhancementStructuralIdentity ResolveOriginPairIdentity(
        IReadOnlyList<(string HomecomingSourceId, HomecomingBoostDiscoveryRecord Discovery)> members)
    {
        var families = members
            .Select(value => HomecomingEnhancementFamilySupport.ClassifySource(
                value.HomecomingSourceId,
                value.Discovery))
            .Distinct()
            .ToArray();
        if (families.Length != 1 || families[0] != ReferenceEnhancementFamily.OriginOrTraining)
        {
            throw new HomecomingEnhancementCandidateException(
                "Origin structural pairs must classify as OriginOrTraining.");
        }

        var applicabilityKey = BuildOriginApplicabilityKey(members[0].Discovery);
        foreach (var member in members.Skip(1))
        {
            if (!string.Equals(
                    applicabilityKey,
                    BuildOriginApplicabilityKey(member.Discovery),
                    StringComparison.Ordinal))
            {
                throw new HomecomingEnhancementCandidateException(
                    "Origin structural pair members do not share identical applicability evidence.");
            }
        }

        var prefixes = members
            .Select(value => ExtractOriginTrainingPrefix(value.HomecomingSourceId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (prefixes.Length != 1)
        {
            throw new HomecomingEnhancementCandidateException(
                "Origin structural pair members do not share one origin/training source prefix.");
        }

        return new NonSetEnhancementStructuralIdentity(
            NonSetEnhancementIdentityEvidenceClass.OriginStructuralPair,
            ReferenceEnhancementFamily.OriginOrTraining,
            prefixes[0] + "|" + applicabilityKey);
    }

    private static NonSetEnhancementStructuralIdentity ResolveSingleSourceIdentity(
        string sourceId,
        HomecomingBoostDiscoveryRecord discovery)
    {
        var family = HomecomingEnhancementFamilySupport.ClassifySource(sourceId, discovery);
        return new NonSetEnhancementStructuralIdentity(
            NonSetEnhancementIdentityEvidenceClass.SingleSource,
            family,
            sourceId);
    }

    private static void ValidateCraftedSeriesInvariants(
        IReadOnlyList<(string HomecomingSourceId, HomecomingBoostDiscoveryRecord Discovery)> members,
        NonSetEnhancementStructuralIdentity identity)
    {
        if (!members.All(value => IsCraftedInventionSeriesId(value.HomecomingSourceId)))
        {
            throw new HomecomingEnhancementCandidateException(
                $"Crafted series '{identity.Key}' contains a source outside the proven Crafted invention series pattern.");
        }

        var typeSets = DistinctNonOriginTypeSets(members);
        var icons = members.Select(value => value.Discovery.Icon).Distinct(StringComparer.Ordinal).ToArray();
        var helps = members
            .Select(value => value.Discovery.DisplayHelpMessageKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var allowed = DistinctBoostsAllowedShapes(members);

        if (typeSets.Length != 1 || icons.Length != 1 || helps.Length != 1 || allowed.Length != 1)
        {
            throw new HomecomingEnhancementCandidateException(
                $"Crafted series '{identity.Key}' failed structural corroboration " +
                $"(types={typeSets.Length}, icons={icons.Length}, help={helps.Length}, allowed={allowed.Length}).");
        }
    }

    private static void ValidateOriginPairInvariants(
        IReadOnlyList<(string HomecomingSourceId, HomecomingBoostDiscoveryRecord Discovery)> members,
        NonSetEnhancementStructuralIdentity identity)
    {
        if (members.Any(value => IsCraftedInventionSeriesId(value.HomecomingSourceId)))
        {
            throw new HomecomingEnhancementCandidateException(
                $"Origin structural pair '{identity.Key}' contains Crafted invention series sources.");
        }

        var typeSets = DistinctNonOriginTypeSets(members);
        var allowed = DistinctBoostsAllowedShapes(members);
        if (typeSets.Length != 1 || allowed.Length != 1)
        {
            throw new HomecomingEnhancementCandidateException(
                $"Origin structural pair '{identity.Key}' does not share one BOOST_TYPE set and boosts_allowed shape.");
        }
    }

    internal static string BuildOriginApplicabilityKey(HomecomingBoostDiscoveryRecord discovery) =>
        BuildNonOriginTypeFingerprint(discovery.NonOriginBoostTypes)
        + "|"
        + BuildBoostsAllowedFingerprint(discovery.BoostsAllowed);

    internal static string BuildNonOriginTypeFingerprint(IReadOnlyList<string> nonOriginBoostTypes) =>
        string.Join('+', nonOriginBoostTypes.OrderBy(value => value, StringComparer.Ordinal));

    internal static string BuildBoostsAllowedFingerprint(IReadOnlyList<string> boostsAllowed) =>
        string.Join('|', boostsAllowed.OrderBy(value => value, StringComparer.Ordinal));

    internal static bool IsCraftedInventionSeriesId(string sourceId) =>
        Regex.IsMatch(
            sourceId,
            @"^Boosts\.Crafted_[A-Za-z0-9_]+(?:_\d+)?\.Crafted_[A-Za-z0-9_]+(?:_\d+)?$",
            RegexOptions.CultureInvariant)
        && !sourceId.Contains("Hamidon", StringComparison.OrdinalIgnoreCase);

    internal static string StripCraftedSeriesBaseId(string sourceId)
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

    internal static string FormatGroupDiagnostic(
        string? appOwnedId,
        NonSetEnhancementStructuralIdentity identity,
        IReadOnlyList<string> sourceIds) =>
        new StringBuilder()
            .Append(appOwnedId ?? "(unassigned)")
            .Append(" evidence=")
            .Append(identity.EvidenceClass)
            .Append(" family=")
            .Append(identity.Family)
            .Append(" identity=")
            .Append(identity.Key)
            .Append(" sources=")
            .Append(string.Join(", ", sourceIds))
            .ToString();

    private static string[] DistinctNonOriginTypeSets(
        IReadOnlyList<(string HomecomingSourceId, HomecomingBoostDiscoveryRecord Discovery)> members) =>
        members
            .Select(value => BuildNonOriginTypeFingerprint(value.Discovery.NonOriginBoostTypes))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string[] DistinctBoostsAllowedShapes(
        IReadOnlyList<(string HomecomingSourceId, HomecomingBoostDiscoveryRecord Discovery)> members) =>
        members
            .Select(value => BuildBoostsAllowedFingerprint(value.Discovery.BoostsAllowed))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

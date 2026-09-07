using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingBadgeCandidateGenerator
{
    internal const string SchemaVersion = "homecoming-badge-candidate-v1";
    internal const string SourceArchive = "assets/live/bin.pigg";
    internal const string SourceMember = "bin/badges.bin";
    internal const string NonPlayerFacingNameSentinel = ".";
    private const string CategoryPathPrefix = "DEFS/BADGES/BADGES_";
    private const string CategoryPathSuffix = ".DEF";

    internal static HomecomingBadgeCandidateDocument Create(
        IReadOnlyList<HomecomingBadgeRecord> sourceRecords,
        HomecomingMessageStore messageStore,
        IReadOnlyList<CurrentBadgeIdentity> currentCatalog,
        string buildVersion,
        string packageRevision)
    {
        ArgumentNullException.ThrowIfNull(sourceRecords);
        ArgumentNullException.ThrowIfNull(messageStore);
        ArgumentNullException.ThrowIfNull(currentCatalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRevision);

        var orderedSource = sourceRecords
            .OrderBy(record => record.HomecomingSourceId, StringComparer.Ordinal)
            .ToArray();
        var duplicateSource = orderedSource
            .GroupBy(record => record.HomecomingSourceId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSource is not null)
        {
            throw new HomecomingBadgeCandidateException(
                $"Badge source ID '{duplicateSource.Key}' is duplicated.");
        }

        var playerFacing = new List<ResolvedBadge>();
        var nonPlayerFacing = 0;
        foreach (var record in orderedSource)
        {
            var heroSentinel = record.HeroNameMessageKey == NonPlayerFacingNameSentinel;
            var villainSentinel = record.VillainNameMessageKey == NonPlayerFacingNameSentinel;
            if (heroSentinel || villainSentinel)
            {
                Require(heroSentinel && villainSentinel,
                    $"Badge '{record.HomecomingSourceId}' has inconsistent player-facing name fields.");
                nonPlayerFacing++;
                continue;
            }

            playerFacing.Add(Resolve(record, messageStore));
        }

        var maximumCurrentId = ValidateCurrentCatalog(currentCatalog);
        var matches = Match(playerFacing, currentCatalog);
        var nextId = maximumCurrentId;
        var candidates = new List<HomecomingBadgeCandidateRecord>(playerFacing.Count);
        foreach (var badge in playerFacing)
        {
            var match = matches.BySourceId[badge.Source.HomecomingSourceId];
            string? appOwnedId;
            if (match.Status == HomecomingBadgeMatchStatus.MatchedExisting)
            {
                appOwnedId = match.CurrentMatches[0].AppOwnedId;
            }
            else if (match.Status == HomecomingBadgeMatchStatus.NewFromHomecoming)
            {
                nextId = NextId(nextId);
                appOwnedId = $"BAD-{nextId:D5}";
            }
            else
            {
                appOwnedId = null;
            }

            candidates.Add(new HomecomingBadgeCandidateRecord(
                appOwnedId,
                badge.Source.HomecomingSourceId,
                badge.Source.NumericIndex,
                badge.Source.BadgeType,
                badge.Source.SourcePath,
                badge.Category,
                badge.Source.HeroNameMessageKey,
                badge.HeroName,
                badge.Source.VillainNameMessageKey,
                badge.VillainName,
                OptionalKey(badge.Source.HeroDescriptionMessageKey),
                badge.HeroDescription,
                OptionalKey(badge.Source.VillainDescriptionMessageKey),
                badge.VillainDescription,
                OptionalValue(badge.Source.HeroIcon),
                OptionalValue(badge.Source.VillainIcon),
                match.Status.ToString(),
                match.CurrentMatches
                    .Select(value => value.AppOwnedId)
                    .Order(StringComparer.Ordinal)
                    .ToArray()));
        }

        var categoryCounts = candidates
            .GroupBy(candidate => candidate.CanonicalCategory, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new HomecomingBadgeCategoryCount(group.Key, group.Count()))
            .ToArray();
        var currentOnly = currentCatalog
            .Where(current => !matches.ReferencedCurrentIds.Contains(current.AppOwnedId))
            .OrderBy(current => current.AppOwnedId, StringComparer.Ordinal)
            .Select(current => new HomecomingCurrentBadgeRecord(
                current.AppOwnedId,
                current.DisplayName,
                current.Category,
                current.HomecomingSourceId))
            .ToArray();

        return new HomecomingBadgeCandidateDocument(
            SchemaVersion,
            new HomecomingBadgeCandidateSource(
                buildVersion,
                packageRevision,
                SourceArchive,
                SourceMember,
                false),
            new HomecomingBadgeCandidateSummary(
                orderedSource.Length,
                candidates.Count,
                nonPlayerFacing,
                candidates.Count,
                candidates.Count(candidate =>
                    candidate.HeroDescription is not null
                    && candidate.VillainDescription is not null),
                categoryCounts.Length,
                categoryCounts,
                candidates.Count(candidate =>
                    candidate.HeroIcon is not null || candidate.VillainIcon is not null),
                candidates.Count(candidate => candidate.MatchStatus == nameof(HomecomingBadgeMatchStatus.MatchedExisting)),
                candidates.Count(candidate => candidate.MatchStatus == nameof(HomecomingBadgeMatchStatus.NewFromHomecoming)),
                currentOnly.Length,
                candidates.Count(candidate => candidate.MatchStatus == nameof(HomecomingBadgeMatchStatus.Ambiguous)),
                0),
            candidates,
            currentOnly);
    }

    internal static IReadOnlyList<CurrentBadgeIdentity> LoadEmbeddedCurrentCatalog()
    {
        var assembly = typeof(ItemReferenceCatalogFactory).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            ItemReferenceCatalogFactory.ProductionCatalogResourceName)
            ?? throw new HomecomingBadgeCandidateException(
                "Embedded production item catalog was not found.");
        using var document = JsonDocument.Parse(stream);

        var records = new List<CurrentBadgeIdentity>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(
                    item.GetProperty("family").GetString(),
                    "Badge",
                    StringComparison.Ordinal))
            {
                continue;
            }

            records.Add(new CurrentBadgeIdentity(
                RequireString(item, "catalogItemId"),
                RequireString(item, "currentDisplayName"),
                OptionalJsonString(item, "subtype"),
                OptionalJsonString(item, "homecomingSourceId")));
        }

        return records;
    }

    private static ResolvedBadge Resolve(
        HomecomingBadgeRecord record,
        HomecomingMessageStore messageStore)
    {
        var category = DeriveCategory(record.SourcePath, record.HomecomingSourceId);
        var heroName = ResolveRequiredMessage(
            messageStore,
            record.HeroNameMessageKey,
            $"Badge '{record.HomecomingSourceId}' hero name");
        var villainName = ResolveRequiredMessage(
            messageStore,
            record.VillainNameMessageKey,
            $"Badge '{record.HomecomingSourceId}' villain name");
        var heroDescription = ResolveOptionalMessage(
            messageStore,
            record.HeroDescriptionMessageKey,
            $"Badge '{record.HomecomingSourceId}' hero description");
        var villainDescription = ResolveOptionalMessage(
            messageStore,
            record.VillainDescriptionMessageKey,
            $"Badge '{record.HomecomingSourceId}' villain description");
        return new ResolvedBadge(
            record,
            category,
            heroName,
            villainName,
            heroDescription,
            villainDescription);
    }

    private static string DeriveCategory(string sourcePath, string sourceId)
    {
        if (!sourcePath.StartsWith(CategoryPathPrefix, StringComparison.Ordinal)
            || !sourcePath.EndsWith(CategoryPathSuffix, StringComparison.Ordinal)
            || sourcePath.Length <= CategoryPathPrefix.Length + CategoryPathSuffix.Length)
        {
            throw new HomecomingBadgeCandidateException(
                $"Badge '{sourceId}' source path '{sourcePath}' does not expose a canonical category.");
        }

        var category = sourcePath[
            CategoryPathPrefix.Length..^CategoryPathSuffix.Length];
        Require(category.All(character =>
                character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'),
            $"Badge '{sourceId}' source path category '{category}' is invalid.");
        return category;
    }

    private static string ResolveRequiredMessage(
        HomecomingMessageStore messageStore,
        string key,
        string field)
    {
        if (!messageStore.TryResolve(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new HomecomingBadgeCandidateException(
                $"{field} message key '{key}' is unresolved.");
        }

        return value;
    }

    private static string? ResolveOptionalMessage(
        HomecomingMessageStore messageStore,
        string key,
        string field)
    {
        if (key.Length == 0)
        {
            return null;
        }

        if (!messageStore.TryResolve(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new HomecomingBadgeCandidateException(
                $"{field} message key '{key}' is unresolved.");
        }

        return value;
    }

    private static MatchResult Match(
        IReadOnlyList<ResolvedBadge> badges,
        IReadOnlyList<CurrentBadgeIdentity> currentCatalog)
    {
        var result = new Dictionary<string, BadgeMatch>(StringComparer.Ordinal);
        var referencedCurrentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var badge in badges)
        {
            var sourceMatches = currentCatalog
                .Where(current => current.HomecomingSourceId is not null
                    && current.HomecomingSourceId.Equals(
                        badge.Source.HomecomingSourceId,
                        StringComparison.Ordinal))
                .ToArray();
            if (sourceMatches.Length > 0)
            {
                AddMatch(result, referencedCurrentIds, badge, sourceMatches);
                continue;
            }

            var names = new[] { badge.HeroName, badge.VillainName }
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);
            var categoryMatches = currentCatalog
                .Where(current => current.Category is not null
                    && current.Category.Equals(badge.Category, StringComparison.Ordinal)
                    && names.Contains(current.DisplayName))
                .ToArray();
            if (categoryMatches.Length > 0)
            {
                var sourceCount = badges.Count(source =>
                    source.Category.Equals(badge.Category, StringComparison.Ordinal)
                    && new[] { source.HeroName, source.VillainName }
                        .Any(names.Contains));
                AddMatch(
                    result,
                    referencedCurrentIds,
                    badge,
                    categoryMatches,
                    forceAmbiguous: sourceCount != 1);
                continue;
            }

            var nameOnlyMatches = currentCatalog
                .Where(current => current.Category is null && names.Contains(current.DisplayName))
                .ToArray();
            if (nameOnlyMatches.Length > 0)
            {
                var sourceCount = badges.Count(source =>
                    new[] { source.HeroName, source.VillainName }.Any(names.Contains));
                AddMatch(
                    result,
                    referencedCurrentIds,
                    badge,
                    nameOnlyMatches,
                    forceAmbiguous: sourceCount != 1);
                continue;
            }

            result.Add(
                badge.Source.HomecomingSourceId,
                new BadgeMatch(HomecomingBadgeMatchStatus.NewFromHomecoming, []));
        }

        return new MatchResult(result, referencedCurrentIds);
    }

    private static void AddMatch(
        IDictionary<string, BadgeMatch> result,
        ISet<string> referencedCurrentIds,
        ResolvedBadge badge,
        IReadOnlyList<CurrentBadgeIdentity> currentMatches,
        bool forceAmbiguous = false)
    {
        foreach (var current in currentMatches)
        {
            referencedCurrentIds.Add(current.AppOwnedId);
        }

        result.Add(
            badge.Source.HomecomingSourceId,
            new BadgeMatch(
                !forceAmbiguous && currentMatches.Count == 1
                    ? HomecomingBadgeMatchStatus.MatchedExisting
                    : HomecomingBadgeMatchStatus.Ambiguous,
                currentMatches));
    }

    private static int ValidateCurrentCatalog(IReadOnlyList<CurrentBadgeIdentity> currentCatalog)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var maximum = 0;
        foreach (var current in currentCatalog)
        {
            Require(ids.Add(current.AppOwnedId),
                $"Current catalog Badge ID '{current.AppOwnedId}' is duplicated.");
            maximum = Math.Max(maximum, ParseBadgeId(current.AppOwnedId));
        }

        return maximum;
    }

    private static int ParseBadgeId(string value)
    {
        if (value.Length != 9
            || !value.StartsWith("BAD-", StringComparison.Ordinal)
            || !int.TryParse(
                value.AsSpan(4),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed)
            || parsed is < 1 or > 99999)
        {
            throw new HomecomingBadgeCandidateException(
                $"Current catalog Badge ID '{value}' is invalid.");
        }

        return parsed;
    }

    private static int NextId(int current)
    {
        var next = checked(current + 1);
        if (next > 99999)
        {
            throw new HomecomingBadgeCandidateException(
                "No BAD- identifier remains available for Homecoming Badge candidates.");
        }

        return next;
    }

    private static string RequireString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new HomecomingBadgeCandidateException(
                $"Current catalog Badge field '{propertyName}' is missing.");
        }

        return property.GetString()!;
    }

    private static string? OptionalJsonString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new HomecomingBadgeCandidateException(
                $"Current catalog Badge field '{propertyName}' is invalid.");
        }

        return property.GetString();
    }

    private static string? OptionalKey(string value) => value.Length == 0 ? null : value;

    private static string? OptionalValue(string value) => value.Length == 0 ? null : value;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new HomecomingBadgeCandidateException(message);
        }
    }

    private sealed record ResolvedBadge(
        HomecomingBadgeRecord Source,
        string Category,
        string HeroName,
        string VillainName,
        string? HeroDescription,
        string? VillainDescription);

    private sealed record BadgeMatch(
        HomecomingBadgeMatchStatus Status,
        IReadOnlyList<CurrentBadgeIdentity> CurrentMatches);

    private sealed record MatchResult(
        IReadOnlyDictionary<string, BadgeMatch> BySourceId,
        IReadOnlySet<string> ReferencedCurrentIds);
}

internal static class HomecomingBadgeCandidateWriter
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
            ?? throw new HomecomingBadgeCandidateException(
                $"Candidate output path '{fullPath}' has no parent directory.");
        var extension = Path.GetExtension(fullPath);
        if (extension.Length == 0)
        {
            extension = ".json";
        }

        return Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(fullPath) + ".badges" + extension);
    }

    internal static byte[] Serialize(HomecomingBadgeCandidateDocument candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(candidate, JsonOptions) + "\n");
    }

    internal static void Write(string outputPath, HomecomingBadgeCandidateDocument candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new HomecomingBadgeCandidateException(
                $"Candidate output path '{fullPath}' has no parent directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(fullPath, Serialize(candidate));
    }
}

internal sealed record CurrentBadgeIdentity(
    string AppOwnedId,
    string DisplayName,
    string? Category,
    string? HomecomingSourceId);

internal sealed record HomecomingBadgeCandidateDocument(
    string SchemaVersion,
    HomecomingBadgeCandidateSource Source,
    HomecomingBadgeCandidateSummary Summary,
    IReadOnlyList<HomecomingBadgeCandidateRecord> Records,
    IReadOnlyList<HomecomingCurrentBadgeRecord> CurrentCatalogNotMatched);

internal sealed record HomecomingBadgeCandidateSource(
    string BuildVersion,
    string PackageRevision,
    string Archive,
    string Member,
    bool CanonicalZoneAssociationAvailable);

internal sealed record HomecomingBadgeCandidateSummary(
    int TotalLiveBadgeRecords,
    int PlayerFacingBadgeRecords,
    int NonPlayerFacingDefinitions,
    int NamesResolved,
    int RecordsWithResolvedDescriptions,
    int CategoryCount,
    IReadOnlyList<HomecomingBadgeCategoryCount> CategoryDistribution,
    int RecordsWithIcons,
    int MatchedExisting,
    int NewFromHomecoming,
    int CurrentCatalogNotMatched,
    int Ambiguous,
    int UnresolvedOrInvalid);

internal sealed record HomecomingBadgeCategoryCount(string Category, int Count);

internal sealed record HomecomingBadgeCandidateRecord(
    string? AppOwnedId,
    string HomecomingSourceId,
    uint NumericIndex,
    uint BadgeType,
    string SourcePath,
    string CanonicalCategory,
    string HeroNameMessageKey,
    string HeroName,
    string VillainNameMessageKey,
    string VillainName,
    string? HeroDescriptionMessageKey,
    string? HeroDescription,
    string? VillainDescriptionMessageKey,
    string? VillainDescription,
    string? HeroIcon,
    string? VillainIcon,
    string MatchStatus,
    IReadOnlyList<string> MatchingExistingAppOwnedIds);

internal sealed record HomecomingCurrentBadgeRecord(
    string AppOwnedId,
    string DisplayName,
    string? Category,
    string? HomecomingSourceId);

internal enum HomecomingBadgeMatchStatus
{
    MatchedExisting,
    NewFromHomecoming,
    Ambiguous
}

internal sealed class HomecomingBadgeCandidateException : Exception
{
    internal HomecomingBadgeCandidateException(string message)
        : base(message)
    {
    }
}

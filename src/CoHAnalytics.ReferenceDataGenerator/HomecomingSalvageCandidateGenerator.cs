using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingSalvageCandidateGenerator
{
    internal const string SchemaVersion = "homecoming-salvage-candidate-v1";
    internal const string SourceArchive = "assets/live/bin.pigg";
    internal const string SourceMember = "bin/salvage.bin";

    internal static HomecomingSalvageCandidateDocument Create(
        IReadOnlyList<HomecomingSalvageRecord> sourceRecords,
        HomecomingMessageStore messageStore,
        IReadOnlyList<CurrentSalvageIdentity> currentCatalog,
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
        var duplicateSourceId = orderedSource
            .GroupBy(record => record.HomecomingSourceId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSourceId is not null)
        {
            throw new HomecomingSalvageCandidateException(
                $"Salvage source ID '{duplicateSourceId.Key}' is duplicated.");
        }

        var currentById = new Dictionary<string, CurrentSalvageIdentity>(StringComparer.Ordinal);
        var maximumCurrentId = 0;
        foreach (var current in currentCatalog)
        {
            if (!currentById.TryAdd(current.AppOwnedId, current))
            {
                throw new HomecomingSalvageCandidateException(
                    $"Current catalog Salvage ID '{current.AppOwnedId}' is duplicated.");
            }

            maximumCurrentId = Math.Max(maximumCurrentId, ParseSalvageId(current.AppOwnedId));
        }

        var resolved = orderedSource
            .Select(record => Resolve(record, messageStore))
            .ToArray();
        var currentByName = currentCatalog
            .GroupBy(item => item.DisplayName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var sourceByName = resolved
            .GroupBy(item => item.DisplayName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var nextId = maximumCurrentId;
        var candidates = new List<HomecomingSalvageCandidateRecord>(resolved.Length);
        foreach (var item in resolved)
        {
            if (!currentByName.TryGetValue(item.DisplayName, out var currentMatches))
            {
                nextId = checked(nextId + 1);
                if (nextId > 99999)
                {
                    throw new HomecomingSalvageCandidateException(
                        "No SAL- identifier remains available for new Homecoming Salvage records.");
                }

                candidates.Add(ToCandidate(
                    item,
                    $"SAL-{nextId:D5}",
                    HomecomingSalvageMatchStatus.NewFromHomecoming,
                    []));
                continue;
            }

            var sourceMatches = sourceByName[item.DisplayName];
            if (currentMatches.Length == 1 && sourceMatches.Length == 1)
            {
                candidates.Add(ToCandidate(
                    item,
                    currentMatches[0].AppOwnedId,
                    HomecomingSalvageMatchStatus.MatchedExisting,
                    [currentMatches[0].AppOwnedId]));
                continue;
            }

            candidates.Add(ToCandidate(
                item,
                null,
                HomecomingSalvageMatchStatus.Ambiguous,
                currentMatches
                    .Select(match => match.AppOwnedId)
                    .Order(StringComparer.Ordinal)
                    .ToArray()));
        }

        var sourceNames = sourceByName.Keys.ToHashSet(StringComparer.Ordinal);
        var unmatchedCurrent = currentCatalog
            .Where(item => !sourceNames.Contains(item.DisplayName))
            .OrderBy(item => item.AppOwnedId, StringComparer.Ordinal)
            .Select(item => new HomecomingSalvageUnmatchedCurrentRecord(
                item.AppOwnedId,
                item.DisplayName))
            .ToArray();

        return new HomecomingSalvageCandidateDocument(
            SchemaVersion,
            new HomecomingSalvageCandidateSource(
                buildVersion,
                packageRevision,
                SourceArchive,
                SourceMember),
            CreateSummary(candidates, unmatchedCurrent),
            candidates,
            unmatchedCurrent);
    }

    internal static IReadOnlyList<CurrentSalvageIdentity> LoadEmbeddedCurrentCatalog()
    {
        var assembly = typeof(ItemReferenceCatalogFactory).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            ItemReferenceCatalogFactory.ProductionCatalogResourceName)
            ?? throw new HomecomingSalvageCandidateException(
                "Embedded production item catalog was not found.");
        using var document = JsonDocument.Parse(stream);

        var records = new List<CurrentSalvageIdentity>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(
                    item.GetProperty("family").GetString(),
                    "Salvage",
                    StringComparison.Ordinal))
            {
                continue;
            }

            records.Add(new CurrentSalvageIdentity(
                RequireString(item, "catalogItemId"),
                RequireString(item, "currentDisplayName")));
        }

        return records;
    }

    private static ResolvedSalvage Resolve(
        HomecomingSalvageRecord record,
        HomecomingMessageStore messageStore)
    {
        if (!messageStore.TryResolve(record.DisplayNameMessageKey, out var displayName))
        {
            throw new HomecomingSalvageCandidateException(
                $"Salvage '{record.HomecomingSourceId}' display-name message key " +
                $"'{record.DisplayNameMessageKey}' is unresolved.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new HomecomingSalvageCandidateException(
                $"Salvage '{record.HomecomingSourceId}' display-name message key " +
                $"'{record.DisplayNameMessageKey}' resolved to an empty value.");
        }

        return new ResolvedSalvage(
            record.HomecomingSourceId,
            record.DisplayNameMessageKey,
            displayName,
            RarityName(record.Rarity),
            CategoryName(record.Category),
            record.Icon);
    }

    private static HomecomingSalvageCandidateRecord ToCandidate(
        ResolvedSalvage item,
        string? appOwnedId,
        HomecomingSalvageMatchStatus status,
        IReadOnlyList<string> matchingExistingIds) =>
        new(
            appOwnedId,
            item.HomecomingSourceId,
            item.DisplayNameMessageKey,
            item.DisplayName,
            item.Rarity,
            item.Category,
            item.Icon,
            status.ToString(),
            matchingExistingIds);

    private static HomecomingSalvageCandidateSummary CreateSummary(
        IReadOnlyList<HomecomingSalvageCandidateRecord> candidates,
        IReadOnlyList<HomecomingSalvageUnmatchedCurrentRecord> unmatchedCurrent) =>
        new(
            candidates.Count,
            candidates.Count,
            candidates.Count(record => record.MatchStatus == nameof(HomecomingSalvageMatchStatus.MatchedExisting)),
            candidates.Count(record => record.MatchStatus == nameof(HomecomingSalvageMatchStatus.NewFromHomecoming)),
            unmatchedCurrent.Count,
            candidates.Count(record => record.MatchStatus == nameof(HomecomingSalvageMatchStatus.Ambiguous)),
            [
                new("Common", candidates.Count(record => record.Rarity == "Common")),
                new("Uncommon", candidates.Count(record => record.Rarity == "Uncommon")),
                new("Rare", candidates.Count(record => record.Rarity == "Rare")),
                new("Very Rare", candidates.Count(record => record.Rarity == "Very Rare"))
            ],
            [
                new("Legacy", candidates.Count(record => record.Category == "Legacy")),
                new("Invention", candidates.Count(record => record.Category == "Invention")),
                new("Special", candidates.Count(record => record.Category == "Special")),
                new("Incarnate", candidates.Count(record => record.Category == "Incarnate"))
            ]);

    private static int ParseSalvageId(string appOwnedId)
    {
        if (appOwnedId.Length != 9
            || !appOwnedId.StartsWith("SAL-", StringComparison.Ordinal)
            || !int.TryParse(
                appOwnedId.AsSpan(4),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value)
            || value is < 1 or > 99999)
        {
            throw new HomecomingSalvageCandidateException(
                $"Current catalog Salvage ID '{appOwnedId}' is invalid.");
        }

        return value;
    }

    private static string RequireString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new HomecomingSalvageCandidateException(
                $"Current catalog Salvage field '{propertyName}' is missing.");
        }

        return property.GetString()!;
    }

    private static string RarityName(HomecomingSalvageRarity rarity) =>
        rarity switch
        {
            HomecomingSalvageRarity.Common => "Common",
            HomecomingSalvageRarity.Uncommon => "Uncommon",
            HomecomingSalvageRarity.Rare => "Rare",
            HomecomingSalvageRarity.VeryRare => "Very Rare",
            _ => throw new ArgumentOutOfRangeException(nameof(rarity), rarity, null)
        };

    private static string CategoryName(HomecomingSalvageCategory category) =>
        category switch
        {
            HomecomingSalvageCategory.Legacy => "Legacy",
            HomecomingSalvageCategory.Invention => "Invention",
            HomecomingSalvageCategory.Special => "Special",
            HomecomingSalvageCategory.Incarnate => "Incarnate",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };

    private sealed record ResolvedSalvage(
        string HomecomingSourceId,
        string DisplayNameMessageKey,
        string DisplayName,
        string Rarity,
        string Category,
        string Icon);
}

internal static class HomecomingSalvageCandidateWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static byte[] Serialize(HomecomingSalvageCandidateDocument candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(candidate, JsonOptions) + "\n");
    }

    internal static void Write(string outputPath, HomecomingSalvageCandidateDocument candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new HomecomingSalvageCandidateException(
                $"Candidate output path '{fullPath}' has no parent directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(fullPath, Serialize(candidate));
    }
}

internal sealed record CurrentSalvageIdentity(string AppOwnedId, string DisplayName);

internal sealed record HomecomingSalvageCandidateDocument(
    string SchemaVersion,
    HomecomingSalvageCandidateSource Source,
    HomecomingSalvageCandidateSummary Summary,
    IReadOnlyList<HomecomingSalvageCandidateRecord> Records,
    IReadOnlyList<HomecomingSalvageUnmatchedCurrentRecord> CurrentCatalogNotMatched);

internal sealed record HomecomingSalvageCandidateSource(
    string BuildVersion,
    string PackageRevision,
    string Archive,
    string Member);

internal sealed record HomecomingSalvageCandidateSummary(
    int SalvageRecords,
    int ResolvedNames,
    int MatchedExisting,
    int NewFromHomecoming,
    int CurrentCatalogNotMatched,
    int Ambiguous,
    IReadOnlyList<HomecomingSalvageCount> RarityCounts,
    IReadOnlyList<HomecomingSalvageCount> CategoryCounts);

internal sealed record HomecomingSalvageCount(string Value, int Count);

internal sealed record HomecomingSalvageCandidateRecord(
    string? AppOwnedId,
    string HomecomingSourceId,
    string DisplayNameMessageKey,
    string DisplayName,
    string Rarity,
    string Category,
    string Icon,
    string MatchStatus,
    IReadOnlyList<string> MatchingExistingAppOwnedIds);

internal sealed record HomecomingSalvageUnmatchedCurrentRecord(
    string AppOwnedId,
    string DisplayName);

internal enum HomecomingSalvageMatchStatus
{
    MatchedExisting,
    NewFromHomecoming,
    Ambiguous
}

internal sealed class HomecomingSalvageCandidateException : Exception
{
    internal HomecomingSalvageCandidateException(string message)
        : base(message)
    {
    }
}

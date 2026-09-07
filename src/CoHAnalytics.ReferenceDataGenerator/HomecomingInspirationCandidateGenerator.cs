using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingInspirationCandidateGenerator
{
    internal const string SchemaVersion = "homecoming-inspiration-candidate-v2";
    internal const string SourceArchive = "assets/live/bin_powers.pigg";
    internal const string SourceMember = "bin/powers.bin";

    internal static HomecomingInspirationCandidateDocument Create(
        IReadOnlyList<HomecomingConcreteInspirationRecord> sourceRecords,
        HomecomingMessageStore messageStore,
        IReadOnlyList<CurrentInspirationIdentity> currentCatalog,
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
            throw new HomecomingInspirationCandidateException(
                $"Inspiration source ID '{duplicateSourceId.Key}' is duplicated.");
        }

        var maximumCurrentId = 0;
        var currentById = new HashSet<string>(StringComparer.Ordinal);
        foreach (var current in currentCatalog)
        {
            if (!currentById.Add(current.AppOwnedId))
            {
                throw new HomecomingInspirationCandidateException(
                    $"Current catalog Inspiration ID '{current.AppOwnedId}' is duplicated.");
            }

            maximumCurrentId = Math.Max(maximumCurrentId, ParseInspirationId(current.AppOwnedId));
        }

        var currentBySourceId = currentCatalog
            .Where(item => !string.IsNullOrWhiteSpace(item.HomecomingSourceId))
            .ToDictionary(item => item.HomecomingSourceId!, StringComparer.Ordinal);
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
        var matchedCurrentIds = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<HomecomingInspirationCandidateRecord>(resolved.Length);
        foreach (var item in resolved)
        {
            if (currentBySourceId.TryGetValue(item.HomecomingSourceId, out var sourceMatch))
            {
                matchedCurrentIds.Add(sourceMatch.AppOwnedId);
                candidates.Add(ToCandidate(
                    item,
                    sourceMatch.AppOwnedId,
                    HomecomingInspirationMatchStatus.MatchedExisting,
                    [sourceMatch.AppOwnedId]));
                continue;
            }

            if (!currentByName.TryGetValue(item.DisplayName, out var currentMatches))
            {
                nextId = NextId(nextId);
                var assignedId = $"INS-{nextId:D5}";
                candidates.Add(ToCandidate(
                    item,
                    assignedId,
                    HomecomingInspirationMatchStatus.NewFromHomecoming,
                    []));
                continue;
            }

            var sourceMatches = sourceByName[item.DisplayName];
            if (currentMatches.Length == 1 && sourceMatches.Length == 1)
            {
                matchedCurrentIds.Add(currentMatches[0].AppOwnedId);
                candidates.Add(ToCandidate(
                    item,
                    currentMatches[0].AppOwnedId,
                    HomecomingInspirationMatchStatus.MatchedExisting,
                    [currentMatches[0].AppOwnedId]));
                continue;
            }

            candidates.Add(ToCandidate(
                item,
                null,
                HomecomingInspirationMatchStatus.Ambiguous,
                currentMatches
                    .Select(match => match.AppOwnedId)
                    .Order(StringComparer.Ordinal)
                    .ToArray()));
        }

        var unmatchedCurrent = currentCatalog
            .Where(item => !matchedCurrentIds.Contains(item.AppOwnedId))
            .OrderBy(item => item.AppOwnedId, StringComparer.Ordinal)
            .Select(item => new HomecomingInspirationUnmatchedCurrentRecord(
                item.AppOwnedId,
                item.DisplayName))
            .ToArray();

        return new HomecomingInspirationCandidateDocument(
            SchemaVersion,
            new HomecomingInspirationCandidateSource(
                buildVersion,
                packageRevision,
                SourceArchive,
                SourceMember),
            new HomecomingInspirationCandidateSummary(
                candidates.Count,
                candidates.Count,
                candidates.Count(record => record.MatchStatus == nameof(HomecomingInspirationMatchStatus.MatchedExisting)),
                candidates.Count(record => record.MatchStatus == nameof(HomecomingInspirationMatchStatus.NewFromHomecoming)),
                unmatchedCurrent.Length,
                candidates.Count(record => record.MatchStatus == nameof(HomecomingInspirationMatchStatus.Ambiguous))),
            candidates,
            unmatchedCurrent);
    }

    internal static IReadOnlyList<CurrentInspirationIdentity> LoadEmbeddedCurrentCatalog()
    {
        var assembly = typeof(ItemReferenceCatalogFactory).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            ItemReferenceCatalogFactory.ProductionCatalogResourceName)
            ?? throw new HomecomingInspirationCandidateException(
                "Embedded production item catalog was not found.");
        using var document = JsonDocument.Parse(stream);

        var records = new List<CurrentInspirationIdentity>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(
                    item.GetProperty("family").GetString(),
                    "Inspiration",
                    StringComparison.Ordinal))
            {
                continue;
            }

            records.Add(new CurrentInspirationIdentity(
                RequireString(item, "catalogItemId"),
                RequireString(item, "currentDisplayName"),
                item.TryGetProperty("homecomingSourceId", out var sourceId)
                    && sourceId.ValueKind == JsonValueKind.String
                    ? sourceId.GetString()
                    : null));
        }

        return records;
    }

    private static ResolvedInspiration Resolve(
        HomecomingConcreteInspirationRecord record,
        HomecomingMessageStore messageStore)
    {
        if (!messageStore.TryResolve(record.DisplayNameMessageKey, out var displayName))
        {
            throw new HomecomingInspirationCandidateException(
                $"Inspiration '{record.HomecomingSourceId}' display-name message key " +
                $"'{record.DisplayNameMessageKey}' is unresolved.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new HomecomingInspirationCandidateException(
                $"Inspiration '{record.HomecomingSourceId}' display-name message key " +
                $"'{record.DisplayNameMessageKey}' resolved to an empty value.");
        }

        return new ResolvedInspiration(
            record.HomecomingSourceId,
            record.DisplayNameMessageKey,
            displayName,
            record.DisplayHelpMessageKey,
            TryResolveOptionalMessage(record.DisplayHelpMessageKey, messageStore, record.HomecomingSourceId, "display help"),
            record.ShortHelpMessageKey,
            TryResolveOptionalMessage(record.ShortHelpMessageKey, messageStore, record.HomecomingSourceId, "short help"),
            record.IconIdentity,
            record.HomecomingCategory,
            record.StandardTier,
            record.InspirationForm);
    }

    private static string? TryResolveOptionalMessage(
        string? messageKey,
        HomecomingMessageStore messageStore,
        string homecomingSourceId,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(messageKey))
        {
            return null;
        }

        if (!messageStore.TryResolve(messageKey, out var value))
        {
            throw new HomecomingInspirationCandidateException(
                $"Inspiration '{homecomingSourceId}' {fieldName} message key '{messageKey}' is unresolved.");
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static HomecomingInspirationCandidateRecord ToCandidate(
        ResolvedInspiration item,
        string? appOwnedId,
        HomecomingInspirationMatchStatus status,
        IReadOnlyList<string> matchingExistingIds) =>
        new(
            appOwnedId,
            item.HomecomingSourceId,
            item.DisplayNameMessageKey,
            item.DisplayName,
            item.DisplayHelpMessageKey,
            item.DisplayHelp,
            item.ShortHelpMessageKey,
            item.ShortHelp,
            item.IconIdentity,
            item.HomecomingCategory,
            item.StandardTier,
            item.InspirationForm,
            status.ToString(),
            matchingExistingIds);

    private sealed record ResolvedInspiration(
        string HomecomingSourceId,
        string DisplayNameMessageKey,
        string DisplayName,
        string? DisplayHelpMessageKey,
        string? DisplayHelp,
        string? ShortHelpMessageKey,
        string? ShortHelp,
        string? IconIdentity,
        string HomecomingCategory,
        string? StandardTier,
        string InspirationForm);

    private static int ParseInspirationId(string appOwnedId)
    {
        if (appOwnedId.Length != 9
            || !appOwnedId.StartsWith("INS-", StringComparison.Ordinal)
            || !int.TryParse(
                appOwnedId.AsSpan(4),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value)
            || value is < 1 or > 99999)
        {
            throw new HomecomingInspirationCandidateException(
                $"Current catalog Inspiration ID '{appOwnedId}' is invalid.");
        }

        return value;
    }

    private static int NextId(int current)
    {
        var next = checked(current + 1);
        if (next > 99999)
        {
            throw new HomecomingInspirationCandidateException(
                "No INS- identifier remains available for new Homecoming Inspiration records.");
        }

        return next;
    }

    private static string RequireString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new HomecomingInspirationCandidateException(
                $"Current catalog Inspiration field '{propertyName}' is missing.");
        }

        return property.GetString()!;
    }
}

internal static class HomecomingInspirationCandidateWriter
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
            ?? throw new HomecomingInspirationCandidateException(
                $"Candidate output path '{fullPath}' has no parent directory.");
        var extension = Path.GetExtension(fullPath);
        if (extension.Length == 0)
        {
            extension = ".json";
        }

        return Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(fullPath) + ".inspirations" + extension);
    }

    internal static byte[] Serialize(HomecomingInspirationCandidateDocument candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(candidate, JsonOptions) + "\n");
    }

    internal static void Write(string outputPath, HomecomingInspirationCandidateDocument candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new HomecomingInspirationCandidateException(
                $"Candidate output path '{fullPath}' has no parent directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(fullPath, Serialize(candidate));
    }
}

internal sealed record CurrentInspirationIdentity(
    string AppOwnedId,
    string DisplayName,
    string? HomecomingSourceId = null);

internal sealed record HomecomingInspirationCandidateDocument(
    string SchemaVersion,
    HomecomingInspirationCandidateSource Source,
    HomecomingInspirationCandidateSummary Summary,
    IReadOnlyList<HomecomingInspirationCandidateRecord> Records,
    IReadOnlyList<HomecomingInspirationUnmatchedCurrentRecord> CurrentCatalogNotMatched);

internal sealed record HomecomingInspirationCandidateSource(
    string BuildVersion,
    string PackageRevision,
    string Archive,
    string Member);

internal sealed record HomecomingInspirationCandidateSummary(
    int HomecomingInspirations,
    int ResolvedNames,
    int MatchedExisting,
    int NewFromHomecoming,
    int CurrentCatalogNotMatched,
    int Ambiguous);

internal sealed record HomecomingInspirationCandidateRecord(
    string? AppOwnedId,
    string HomecomingSourceId,
    string DisplayNameMessageKey,
    string DisplayName,
    string? DisplayHelpMessageKey,
    string? DisplayHelp,
    string? ShortHelpMessageKey,
    string? ShortHelp,
    string? IconIdentity,
    string HomecomingCategory,
    string? StandardTier,
    string InspirationForm,
    string MatchStatus,
    IReadOnlyList<string> MatchingExistingAppOwnedIds);

internal sealed record HomecomingInspirationUnmatchedCurrentRecord(
    string AppOwnedId,
    string DisplayName);

internal enum HomecomingInspirationMatchStatus
{
    MatchedExisting,
    NewFromHomecoming,
    Ambiguous
}

internal sealed class HomecomingInspirationCandidateException : Exception
{
    internal HomecomingInspirationCandidateException(string message)
        : base(message)
    {
    }
}

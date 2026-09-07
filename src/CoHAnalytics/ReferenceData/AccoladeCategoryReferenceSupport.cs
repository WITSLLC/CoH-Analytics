using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoHAnalytics.ReferenceData;

/// <summary>Loads Codex Accolade presentation categories for Reference browse grouping.</summary>
public static class AccoladeCategoryReferenceSupport
{
    public const string ProductionResourceName = "CoHAnalytics.ReferenceData.accolade-categories.v1.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static AccoladeCategoryReferenceSnapshot? _cachedProductionSnapshot;

    public static AccoladeCategoryReferenceSnapshot LoadEmbeddedProduction()
    {
        if (_cachedProductionSnapshot is not null)
        {
            return _cachedProductionSnapshot;
        }

        var assembly = typeof(AccoladeCategoryReferenceSupport).Assembly;
        using var stream = assembly.GetManifestResourceStream(ProductionResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded accolade category resource '{ProductionResourceName}' was not found.");
        }

        _cachedProductionSnapshot = Load(stream);
        return _cachedProductionSnapshot;
    }

    internal static AccoladeCategoryReferenceSnapshot Load(Stream stream)
    {
        var document = JsonSerializer.Deserialize<AccoladeCategoryReferenceDocument>(stream, ReadOptions)
            ?? throw new InvalidOperationException("Accolade category document is empty.");

        var categories = (document.Categories ?? [])
            .Where(category => !string.IsNullOrWhiteSpace(category.Id)
                && !string.IsNullOrWhiteSpace(category.DisplayName))
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Id, StringComparer.Ordinal)
            .Select(category => new AccoladePresentationCategoryRecord(
                category.Id!,
                category.DisplayName!,
                category.SortOrder))
            .ToArray();

        var badgeCategoryById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var accolade in document.Accolades ?? [])
        {
            if (string.IsNullOrWhiteSpace(accolade.BadgeId)
                || string.IsNullOrWhiteSpace(accolade.CategoryId))
            {
                continue;
            }

            badgeCategoryById[accolade.BadgeId] = accolade.CategoryId;
        }

        return new AccoladeCategoryReferenceSnapshot(categories, badgeCategoryById);
    }

    private sealed class AccoladeCategoryReferenceDocument
    {
        public List<AccoladeCategoryDocument>? Categories { get; set; }

        public List<AccoladeCategoryAssignmentDocument>? Accolades { get; set; }
    }

    private sealed class AccoladeCategoryDocument
    {
        public string? Id { get; set; }

        public string? DisplayName { get; set; }

        public int SortOrder { get; set; }
    }

    private sealed class AccoladeCategoryAssignmentDocument
    {
        public string? BadgeId { get; set; }

        public string? CategoryId { get; set; }
    }
}

namespace CoHAnalytics.ReferenceData;

public sealed record AccoladePresentationCategoryRecord(
    string Id,
    string DisplayName,
    int SortOrder);

public sealed class AccoladeCategoryReferenceSnapshot
{
    public AccoladeCategoryReferenceSnapshot(
        IReadOnlyList<AccoladePresentationCategoryRecord> categories,
        IReadOnlyDictionary<string, string> badgeCategoryById)
    {
        Categories = categories;
        BadgeCategoryById = badgeCategoryById;
    }

    public IReadOnlyList<AccoladePresentationCategoryRecord> Categories { get; }

    public IReadOnlyDictionary<string, string> BadgeCategoryById { get; }

    public const string FallbackCategoryId = "general";

    public string ResolveCategoryId(string badgeId) =>
        BadgeCategoryById.TryGetValue(badgeId, out var categoryId)
            ? categoryId
            : FallbackCategoryId;
}

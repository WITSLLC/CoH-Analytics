namespace CoHAnalytics.ReferenceData;

internal static class ItemReferenceLookup
{
    public static string NormalizeLookupKey(string text) =>
        text.Trim().ToUpperInvariant();
}

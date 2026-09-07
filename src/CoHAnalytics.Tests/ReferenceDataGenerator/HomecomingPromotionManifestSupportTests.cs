using System.Text.Json;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingPromotionManifestSupportTests
{
    [Fact]
    public void ApplyPromotionRelease_PreservesNewerCanonicalManifest()
    {
        var document = CreateDocument("item-ref-99.0.0");
        var startingManifest = document.Manifest;

        var applied = HomecomingPromotionManifestSupport.ApplyPromotionRelease(
            document,
            "item-ref-3.0.0",
            "badge-revision",
            "badge-note",
            "badge-build");

        Assert.False(applied);
        Assert.Same(startingManifest, document.Manifest);
        AssertFutureManifest(document.Manifest!);
    }

    [Fact]
    public void ApplyPromotionRelease_PreservesUnrecognizedCanonicalVersion()
    {
        var document = CreateDocument("future-catalog-version");
        var startingManifest = document.Manifest;

        var applied = HomecomingPromotionManifestSupport.ApplyPromotionRelease(
            document,
            "item-ref-3.0.0",
            "badge-revision",
            "badge-note",
            "badge-build");

        Assert.False(applied);
        Assert.Same(startingManifest, document.Manifest);
        Assert.Equal("future-catalog-version", document.Manifest!.CatalogVersion);
        Assert.Equal(PromotionManifestOwnershipTestSupport.FutureSourceRevision, document.Manifest.SourceRevision);
    }

    [Fact]
    public void ApplyPromotionRelease_AdvancesOlderCatalogAndRecordsPromotionEvidence()
    {
        var document = CreateDocument("item-ref-2.9.0");

        var applied = HomecomingPromotionManifestSupport.ApplyPromotionRelease(
            document,
            "item-ref-3.0.0",
            "badge-revision",
            "badge-note",
            "badge-build");

        Assert.True(applied);
        Assert.Equal("item-ref-3.0.0", document.Manifest!.CatalogVersion);
        Assert.Equal("badge-revision", document.Manifest.SourceRevision);
        Assert.Equal("badge-note", document.Manifest.SourceNotes);
        Assert.Equal("badge-build", document.Manifest.HomecomingCompatibility!.BuildMin);
        Assert.Equal("badge-build", document.Manifest.HomecomingCompatibility.BuildMax);
    }

    [Fact]
    public void ApplyPromotionRelease_RefreshesEvidenceForMatchingRelease()
    {
        var document = CreateDocument("item-ref-3.0.0");

        var applied = HomecomingPromotionManifestSupport.ApplyPromotionRelease(
            document,
            "item-ref-3.0.0",
            "badge-revision",
            "badge-note",
            "badge-build");

        Assert.True(applied);
        Assert.Equal("item-ref-3.0.0", document.Manifest!.CatalogVersion);
        Assert.Equal("badge-revision", document.Manifest.SourceRevision);
        Assert.Equal("badge-note", document.Manifest.SourceNotes);
        Assert.Equal("badge-build", document.Manifest.HomecomingCompatibility!.BuildMin);
        Assert.Equal("badge-build", document.Manifest.HomecomingCompatibility.BuildMax);
    }

    private static ItemReferenceCatalogDocument CreateDocument(string catalogVersion) =>
        new()
        {
            Manifest = new ItemReferenceManifestDocument
            {
                CatalogVersion = catalogVersion,
                SourceRevision = PromotionManifestOwnershipTestSupport.FutureSourceRevision,
                SourceNotes = PromotionManifestOwnershipTestSupport.FutureSourceNotes,
                HomecomingCompatibility = new ItemReferenceHomecomingCompatibilityDocument
                {
                    BuildMin = PromotionManifestOwnershipTestSupport.FutureBuildMin,
                    BuildMax = PromotionManifestOwnershipTestSupport.FutureBuildMax
                }
            }
        };

    private static void AssertFutureManifest(ItemReferenceManifestDocument manifest)
    {
        Assert.Equal(PromotionManifestOwnershipTestSupport.FutureCatalogVersion, manifest.CatalogVersion);
        Assert.Equal(PromotionManifestOwnershipTestSupport.FutureSourceRevision, manifest.SourceRevision);
        Assert.Equal(PromotionManifestOwnershipTestSupport.FutureSourceNotes, manifest.SourceNotes);
        Assert.Equal(PromotionManifestOwnershipTestSupport.FutureBuildMin, manifest.HomecomingCompatibility!.BuildMin);
        Assert.Equal(PromotionManifestOwnershipTestSupport.FutureBuildMax, manifest.HomecomingCompatibility.BuildMax);
    }
}

internal static class PromotionManifestOwnershipTestSupport
{
    internal const string FutureCatalogVersion = "item-ref-99.0.0";
    internal const string FutureSourceRevision = "future-test-revision";
    internal const string FutureSourceNotes = "future-test-note";
    internal const string FutureBuildMin = "future-test-build-min";
    internal const string FutureBuildMax = "future-test-build-max";

    internal static void WriteFutureManifest(string catalogPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("manifest"))
                {
                    writer.WritePropertyName("manifest");
                    writer.WriteStartObject();
                    writer.WriteString("catalogVersion", FutureCatalogVersion);
                    writer.WritePropertyName("homecomingCompatibility");
                    writer.WriteStartObject();
                    writer.WriteString("buildMin", FutureBuildMin);
                    writer.WriteString("buildMax", FutureBuildMax);
                    writer.WriteEndObject();
                    writer.WriteString("sourceRevision", FutureSourceRevision);
                    writer.WriteString("sourceNotes", FutureSourceNotes);
                    writer.WriteEndObject();
                }
                else
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        File.WriteAllBytes(catalogPath, [.. output.ToArray(), (byte)'\n']);
    }

    internal static void AssertFutureManifestPreserved(string catalogPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
        var manifest = document.RootElement.GetProperty("manifest");
        Assert.Equal(FutureCatalogVersion, manifest.GetProperty("catalogVersion").GetString());
        Assert.Equal(FutureSourceRevision, manifest.GetProperty("sourceRevision").GetString());
        Assert.Equal(FutureSourceNotes, manifest.GetProperty("sourceNotes").GetString());
        var compatibility = manifest.GetProperty("homecomingCompatibility");
        Assert.Equal(FutureBuildMin, compatibility.GetProperty("buildMin").GetString());
        Assert.Equal(FutureBuildMax, compatibility.GetProperty("buildMax").GetString());
    }

    internal static IReadOnlyList<(string CatalogItemId, string? DisplayName)> ReadStableIds(
        string catalogPath,
        string family)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
        return document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Where(item => item.GetProperty("family").GetString() == family)
            .Select(item => (
                item.GetProperty("catalogItemId").GetString()!,
                item.GetProperty("currentDisplayName").GetString()))
            .OrderBy(item => item.Item1, StringComparer.Ordinal)
            .ToArray();
    }
}

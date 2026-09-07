using System.Text;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingInspirationPromotionSupportTests
{
    [Theory]
    [InlineData("Single", "Single")]
    [InlineData("Dual", "Dual")]
    [InlineData("Team", "Team")]
    [InlineData("TeamDual", "TeamDual")]
    [InlineData("Special/Event", "Special")]
    public void MapSubtype_UsesAppOwnedSubtypeSemantics(string inspirationForm, string expectedSubtype)
    {
        Assert.Equal(expectedSubtype, HomecomingInspirationPromotionSupport.MapSubtype(inspirationForm));
    }

    [Fact]
    public void Apply_PreservesExistingSubtypeAndPromotesMetadata()
    {
        var document = new ItemReferenceCatalogDocument
        {
            Items =
            [
                new ItemReferenceRecordDocument
                {
                    CatalogItemId = "INS-00039",
                    Family = nameof(ReferenceItemFamily.Inspiration),
                    Subtype = "Dual",
                    CurrentDisplayName = "Protected",
                    ActiveStatus = nameof(ReferenceActiveStatus.Active),
                    VerificationStatus = nameof(ReferenceVerificationStatus.VerifiedMultiSource)
                },
                new ItemReferenceRecordDocument
                {
                    CatalogItemId = "INS-00075",
                    Family = nameof(ReferenceItemFamily.Inspiration),
                    Subtype = "Special",
                    CurrentDisplayName = "Essence of the Earth",
                    ActiveStatus = nameof(ReferenceActiveStatus.Active),
                    VerificationStatus = nameof(ReferenceVerificationStatus.VerifiedDirect)
                }
            ],
            Aliases =
            [
                new ItemReferenceAliasRecordDocument
                {
                    CatalogItemId = "INS-00039",
                    Locale = "en",
                    Text = "Protected",
                    NameKind = "LogReceipt",
                    IsPreferred = true
                }
            ]
        };

        var candidate = new HomecomingInspirationCandidateDocument(
            HomecomingInspirationCandidateGenerator.SchemaVersion,
            new HomecomingInspirationCandidateSource("Build", "Revision", "archive", "member"),
            new HomecomingInspirationCandidateSummary(2, 2, 1, 1, 0, 0),
            [
                Candidate(
                    "INS-00039",
                    "Inspirations.Large_Dual.Protected",
                    "Protected",
                    "Large_Dual",
                    "Large",
                    "Dual",
                    "Inspiration_Dual_Def_Res_Lvl_3.tga"),
                Candidate(
                    "INS-00076",
                    "Inspirations.Special.NewSpecial",
                    "New Special",
                    "Special",
                    null,
                    "Special/Event",
                    "Inspiration_Special.tga")
            ],
            []);

        var artifacts = HomecomingInspirationPromotionSupport.Apply(document, candidate);

        Assert.Equal(2, artifacts.PromotedCount);
        Assert.Equal(1, artifacts.ExistingCount);
        Assert.Equal(1, artifacts.NewCount);

        var protectedItem = Assert.Single(document.Items, item => item.CatalogItemId == "INS-00039");
        Assert.Equal("Dual", protectedItem.Subtype);
        Assert.Equal("Inspirations.Large_Dual.Protected", protectedItem.HomecomingSourceId);
        Assert.Equal("Large", protectedItem.InspirationStandardTier);
        Assert.Equal("Dual", protectedItem.InspirationForm);
        Assert.Equal("Inspiration_Dual_Def_Res_Lvl_3.tga", protectedItem.Icon);

        var newItem = Assert.Single(document.Items, item => item.CatalogItemId == "INS-00076");
        Assert.Equal("Special", newItem.Subtype);
        Assert.Null(newItem.InspirationStandardTier);

        Assert.Contains(
            document.Aliases,
            alias => alias.CatalogItemId == "INS-00076" && alias.Text == "New Special");
    }

    [Fact]
    public void Apply_RejectsAmbiguousCandidate()
    {
        var document = new ItemReferenceCatalogDocument
        {
            Items =
            [
                new ItemReferenceRecordDocument
                {
                    CatalogItemId = "INS-00001",
                    Family = nameof(ReferenceItemFamily.Inspiration),
                    Subtype = "Single",
                    CurrentDisplayName = "Insight",
                    ActiveStatus = nameof(ReferenceActiveStatus.Active),
                    VerificationStatus = nameof(ReferenceVerificationStatus.VerifiedMultiSource)
                }
            ]
        };

        var candidate = new HomecomingInspirationCandidateDocument(
            HomecomingInspirationCandidateGenerator.SchemaVersion,
            new HomecomingInspirationCandidateSource("Build", "Revision", "archive", "member"),
            new HomecomingInspirationCandidateSummary(1, 1, 0, 0, 0, 1),
            [
                Candidate(
                    null,
                    "Inspirations.Small.Insight",
                    "Insight",
                    "Small",
                    "Small",
                    "Single",
                    "Inspiration_Small.tga",
                    "Ambiguous")
            ],
            []);

        Assert.Throws<HomecomingInspirationPromotionException>(() =>
            HomecomingInspirationPromotionSupport.Apply(document, candidate));
    }

    private static HomecomingInspirationCandidateRecord Candidate(
        string? appOwnedId,
        string homecomingSourceId,
        string displayName,
        string category,
        string? standardTier,
        string inspirationForm,
        string icon,
        string matchStatus = "MatchedExisting") =>
        new(
            appOwnedId,
            homecomingSourceId,
            "P_NAME",
            displayName,
            "P_HELP",
            "Help text",
            "P_SHORT",
            "Short help",
            icon,
            category,
            standardTier,
            inspirationForm,
            matchStatus,
            appOwnedId is null ? [] : [appOwnedId]);
}

[Collection(LiveInstallPromotionCollection.Name)]
public sealed class HomecomingInspirationPromotionCommandTests
{
    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_PromotesAllCurrentHomecomingInspirations()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "coh-inspiration-promotion-" + Guid.NewGuid().ToString("N"));
        var catalogPath = Path.Combine(tempRoot, "item-catalog.v1.json");
        try
        {
            Directory.CreateDirectory(tempRoot);
            using (var embedded = typeof(ItemReferenceCatalogFactory).Assembly.GetManifestResourceStream(
                       ItemReferenceCatalogFactory.ProductionCatalogResourceName)
                   ?? throw new InvalidOperationException("Embedded production catalog missing."))
            using (var output = File.Create(catalogPath))
            {
                embedded.CopyTo(output);
            }

            PromotionManifestOwnershipTestSupport.WriteFutureManifest(catalogPath);
            var beforeStableIds = PromotionManifestOwnershipTestSupport.ReadStableIds(catalogPath, "Inspiration");
            var candidatePath = Path.Combine(tempRoot, "candidate.inspirations.json");
            var importExit = HomecomingImportCommand.Run(
                [
                    "--install",
                    LiveInstallRoot,
                    "--output",
                    Path.Combine(tempRoot, "salvage.candidate.json")
                ],
                TextWriter.Null,
                TextWriter.Null);
            Assert.Equal(0, importExit);

            File.Copy(
                HomecomingInspirationCandidateWriter.GetOutputPath(
                    Path.Combine(tempRoot, "salvage.candidate.json")),
                candidatePath);

            var first = HomecomingInspirationPromotionCommand.Promote(candidatePath, catalogPath);
            var second = HomecomingInspirationPromotionCommand.Promote(candidatePath, catalogPath);
            Assert.Equal(first.CatalogSha256, second.CatalogSha256);
            Assert.Equal(96, first.Artifacts.PromotedCount);
            Assert.Equal(96, first.Artifacts.ExistingCount);
            Assert.Equal(0, first.Artifacts.NewCount);
            PromotionManifestOwnershipTestSupport.AssertFutureManifestPreserved(catalogPath);
            Assert.Equal(
                beforeStableIds,
                PromotionManifestOwnershipTestSupport.ReadStableIds(catalogPath, "Inspiration"));

            using var validationStream = File.OpenRead(catalogPath);
            var load = ItemReferenceCatalogLoader.Load(validationStream);
            Assert.True(load.Succeeded, load.FailureReason);

            var promotedLoad = ItemReferenceCatalogLoader.Load(File.OpenRead(catalogPath));
            Assert.True(promotedLoad.Succeeded, promotedLoad.FailureReason);
            Assert.Equal(96, promotedLoad.Items!.Values.Count(item =>
                item.Family == ReferenceItemFamily.Inspiration));

            var promotedCatalog = ItemReferenceCatalog.FromLoadResult(promotedLoad);

            var protectedItem = promotedLoad.Items!["INS-00039"];
            Assert.Equal("Inspirations.Large_Dual.Protected", protectedItem.HomecomingSourceId);
            Assert.Equal("Large_Dual", protectedItem.HomecomingCategory);
            Assert.Equal("Large", protectedItem.InspirationStandardTier);
            Assert.Equal("Dual", protectedItem.InspirationForm);
            Assert.Equal("Inspiration_Dual_Def_Res_Lvl_3.tga", protectedItem.Icon);

            var revitalize = promotedLoad.Items!["INS-00041"];
            Assert.Equal("Inspirations.Small_Dual.Revitalize", revitalize.HomecomingSourceId);
            Assert.Equal("Small_Dual", revitalize.HomecomingCategory);
            Assert.Equal("Small", revitalize.InspirationStandardTier);
            Assert.Equal("Dual", revitalize.InspirationForm);
            Assert.Equal("Inspiration_Dual_Health_End_Lvl_1.tga", revitalize.Icon);

            var taxonomy = new ItemReferenceReceivedItemTaxonomyCatalog(promotedCatalog);
            Assert.True(taxonomy.TryClassifyInspiration("Protected", out var protectedMetadata));
            Assert.Equal("INS-00039", protectedMetadata!.CatalogItemId);
            Assert.Equal("Dual", protectedMetadata.InspirationForm);
            Assert.True(taxonomy.TryClassifyInspiration("Luck Imbuement", out _));
            Assert.True(taxonomy.TryClassifyInspiration("Revitalize", out _));
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

using CoHAnalytics.Homecoming;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingInspirationDiscoveryTests
{
    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;
    private const string PowersArchive = @"assets\live\bin_powers.pigg";
    private const string PowersMember = "bin/powers.bin";
    private const string MessagesArchive = @"assets\live\bin.pigg";
    private const string MessagesMember = "bin/clientmessages-en.bin";

    [Fact]
    public void ReadInspirations_DiscoveryFixture_RecoversIconAndHelpKeys()
    {
        var data = HomecomingBinaryFixtureBuilder.CreatePowersDiscovery(
            new SyntheticInspirationDiscoveryRecord(
                "Inspirations.Large_Dual.Protected",
                "P_PROTECTED",
                "P_PROTECTED_HELP",
                "P_PROTECTED_SHORT",
                "Inspiration_Dual_Def_Res_Lvl_3.tga"));

        var record = Assert.Single(HomecomingPowersReader.ReadInspirations(data));
        Assert.Equal("Inspirations.Large_Dual.Protected", record.HomecomingSourceId);
        Assert.Equal("P_PROTECTED", record.DisplayNameMessageKey);
        Assert.Equal("P_PROTECTED_HELP", record.DisplayHelpMessageKey);
        Assert.Equal("P_PROTECTED_SHORT", record.ShortHelpMessageKey);
        Assert.Equal("Inspiration_Dual_Def_Res_Lvl_3.tga", record.IconIdentity);
        Assert.Equal("Large_Dual", record.HomecomingCategory);
        Assert.Equal("Large", record.StandardTier);
        Assert.Equal("Dual", record.InspirationForm);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_ProtectedAndRevitalize_MatchAuthoritativeMetadata()
    {
        var powers = ReadLivePowers();
        var messages = ReadLiveMessages();
        var inspirations = HomecomingPowersReader.ReadInspirations(powers);
        Assert.InRange(inspirations.Count, 90, 110);

        var protectedRecord = Assert.Single(
            inspirations,
            record => record.HomecomingSourceId == "Inspirations.Large_Dual.Protected");
        Assert.True(messages.TryResolve(protectedRecord.DisplayNameMessageKey, out var protectedName));
        Assert.Equal("Protected", protectedName);
        Assert.Equal("Large_Dual", protectedRecord.HomecomingCategory);
        Assert.Equal("Large", protectedRecord.StandardTier);
        Assert.Equal("Dual", protectedRecord.InspirationForm);
        Assert.Equal("Inspiration_Dual_Def_Res_Lvl_3.tga", protectedRecord.IconIdentity);

        var revitalize = Assert.Single(
            inspirations,
            record => record.HomecomingSourceId == "Inspirations.Small_Dual.Revitalize");
        Assert.True(messages.TryResolve(revitalize.DisplayNameMessageKey, out var revitalizeName));
        Assert.Equal("Revitalize", revitalizeName);
        Assert.Equal("Small_Dual", revitalize.HomecomingCategory);
        Assert.Equal("Small", revitalize.StandardTier);
        Assert.Equal("Dual", revitalize.InspirationForm);
        Assert.Equal("Inspiration_Dual_Health_End_Lvl_1.tga", revitalize.IconIdentity);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_InspirationInventory_ReportsCategoryAndIconCoverage()
    {
        var inspirations = HomecomingPowersReader.ReadInspirations(ReadLivePowers());
        var withIcons = inspirations.Count(record => !string.IsNullOrWhiteSpace(record.IconIdentity));
        var uniqueIcons = inspirations
            .Select(record => record.IconIdentity!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        Assert.Equal(inspirations.Count, withIcons);
        Assert.InRange(uniqueIcons, 80, 95);
    }

    private static byte[] ReadLivePowers() =>
        HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, PowersArchive),
            PowersMember);

    private static HomecomingMessageStore ReadLiveMessages() =>
        HomecomingMessageStoreReader.Read(
            HomecomingPiggMemberReader.ReadMember(
                Path.Combine(LiveInstallRoot, MessagesArchive),
                MessagesMember));
}

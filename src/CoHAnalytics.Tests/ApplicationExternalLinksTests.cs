namespace CoHAnalytics.Tests;

public sealed class ApplicationExternalLinksTests
{
    [Fact]
    public void Project_home_uri_points_to_github_repository()
    {
        Assert.Equal("https://github.com/WITSLLC/CoH-Analytics", ApplicationExternalLinks.ProjectHomeUri);
    }

    [Fact]
    public void Report_bug_uri_points_to_github_issue_creation()
    {
        Assert.Equal(
            "https://github.com/WITSLLC/CoH-Analytics/issues/new",
            ApplicationExternalLinks.ReportBugUri);
    }

    [Fact]
    public void Support_app_uri_points_to_hosted_paypal_donation_page()
    {
        Assert.Equal(
            "https://www.paypal.com/donate/?hosted_button_id=Q263DCVKTKM5W",
            ApplicationExternalLinks.SupportAppUri);
        Assert.Contains(
            ApplicationExternalLinks.SupportAppHostedButtonId,
            ApplicationExternalLinks.SupportAppQrResourcePath,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Support_app_monogram_resource_is_packaged_from_official_paypal_asset()
    {
        const string resourceManifestName = "CoHAnalytics.g.resources";
        var expectedResourceKey = ApplicationExternalLinks.SupportAppMonogramResourcePath
            .Split(";component/", StringSplitOptions.None)[1]
            .ToLowerInvariant();
        var assembly = typeof(ApplicationExternalLinks).Assembly;
        using var resourceStream = assembly.GetManifestResourceStream(resourceManifestName);

        Assert.NotNull(resourceStream);
        using var resources = new System.Resources.ResourceReader(resourceStream);
        var resourceKeys = resources
            .Cast<System.Collections.DictionaryEntry>()
            .Select(entry => Assert.IsType<string>(entry.Key));

        Assert.Contains(expectedResourceKey, resourceKeys);
    }

    [Fact]
    public void Support_app_qr_resource_is_packaged_for_the_authoritative_hosted_button()
    {
        const string resourceManifestName = "CoHAnalytics.g.resources";
        var expectedResourceKey = ApplicationExternalLinks.SupportAppQrResourcePath
            .Split(";component/", StringSplitOptions.None)[1]
            .ToLowerInvariant();
        var assembly = typeof(ApplicationExternalLinks).Assembly;
        using var resourceStream = assembly.GetManifestResourceStream(resourceManifestName);

        Assert.NotNull(resourceStream);
        using var resources = new System.Resources.ResourceReader(resourceStream);
        var resourceKeys = resources
            .Cast<System.Collections.DictionaryEntry>()
            .Select(entry => Assert.IsType<string>(entry.Key));

        Assert.Contains(expectedResourceKey, resourceKeys);
    }

    [Fact]
    public void Homecoming_uri_points_to_official_forums_site()
    {
        Assert.Equal("https://forums.homecomingservers.com/", ApplicationExternalLinks.HomecomingUri);
    }

    [Fact]
    public void Documentation_destination_is_not_configured()
    {
        Assert.False(ApplicationExternalLinks.HasDocumentationDestination);
    }
}

namespace CoHAnalytics.Tests;

public sealed class ApplicationLegalDocumentsTests
{
    [Fact]
    public void TryResolveLicensePath_finds_repo_license_from_nested_start_directory()
    {
        var start = Path.Combine(
            AppContext.BaseDirectory,
            "nested",
            "build",
            "out");
        Directory.CreateDirectory(start);

        var resolved = ApplicationLegalDocuments.TryResolveLicensePath(start, out var path);

        Assert.True(resolved);
        Assert.True(File.Exists(path));
        Assert.Equal("LICENSE", Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Source-Available License", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_license_uri_points_to_official_project_license()
    {
        Assert.Equal(
            "https://github.com/WITSLLC/CoH-Analytics/blob/main/LICENSE",
            ApplicationLegalDocuments.RepositoryLicenseUri);
    }

    [Fact]
    public void Support_app_uri_remains_the_configured_paypal_destination()
    {
        Assert.Equal(
            "https://www.paypal.com/donate/?hosted_button_id=Q263DCVKTKM5W",
            ApplicationExternalLinks.SupportAppUri);
    }
}

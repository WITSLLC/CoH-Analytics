using CoHAnalytics.Updates;

namespace CoHAnalytics.Tests.Updates;

public sealed class UpdateCheckServiceTests
{
    private static readonly ReleaseVersion Beta012 = Parse("0.1.2-beta");

    [Fact]
    public async Task Highest_valid_release_is_selected_independent_of_response_order()
    {
        var service = Create(
            Beta012,
            DeploymentType.SourceBuild,
            Release("v0.1.3-beta", prerelease: true),
            Release("v0.1.2-beta", prerelease: true),
            Release("v0.2.0-beta", prerelease: true));

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("0.2.0-beta", result.RemoteVersion!.CanonicalText);
    }

    [Fact]
    public async Task Draft_unpublished_invalid_and_prerelease_mismatches_are_ignored()
    {
        var service = Create(
            Beta012,
            DeploymentType.SourceBuild,
            Release("v9.0.0", draft: true),
            Release("v8.0.0", published: false),
            Release("v0.1-beta.1", prerelease: true),
            Release("v7.0.0-beta", prerelease: false),
            Release("v6.0.0", prerelease: true),
            Release("v0.1.3-beta", prerelease: true));

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal("0.1.3-beta", result.RemoteVersion!.CanonicalText);
    }

    [Fact]
    public async Task Beta_build_considers_beta_and_stable_releases()
    {
        var service = Create(
            Beta012,
            DeploymentType.SourceBuild,
            Release("v0.1.3-beta", prerelease: true),
            Release("v0.1.3"));

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal("0.1.3", result.RemoteVersion!.CanonicalText);
    }

    [Fact]
    public async Task Stable_build_excludes_beta_releases()
    {
        var service = Create(
            Parse("1.0.0"),
            DeploymentType.SourceBuild,
            Release("v2.0.0-beta", prerelease: true),
            Release("v1.0.0"));

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Current, result.Status);
        Assert.Equal("1.0.0", result.RemoteVersion!.CanonicalText);
    }

    [Theory]
    [InlineData("v0.1.2-beta", UpdateCheckStatus.Current)]
    [InlineData("v0.1.1-beta", UpdateCheckStatus.Current)]
    [InlineData("v0.1.3-beta", UpdateCheckStatus.UpdateAvailable)]
    public async Task Same_older_and_newer_versions_return_expected_result(string tag, UpdateCheckStatus expected)
    {
        var result = await Create(Beta012, DeploymentType.SourceBuild, Release(tag, prerelease: true))
            .CheckAsync(CancellationToken.None);

        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData(DeploymentType.WindowsInstaller, "CoH-Analytics-0.1.3-beta-win-x64.msi", "Download Installer")]
    [InlineData(DeploymentType.PortableSelfContained, "CoH-Analytics-0.1.3-beta-win-x64-self-contained.zip", "Download Portable ZIP")]
    [InlineData(DeploymentType.PortableFrameworkDependent, "CoH-Analytics-0.1.3-beta-win-x64-framework-dependent.zip", "Download Framework-dependent ZIP")]
    public async Task Packaged_deployment_maps_to_exact_asset(
        DeploymentType deploymentType,
        string expectedAsset,
        string expectedLabel)
    {
        var release = Release(
            "v0.1.3-beta",
            prerelease: true,
            assets: [Asset("v0.1.3-beta", expectedAsset)]);

        var result = await Create(Beta012, deploymentType, release).CheckAsync(CancellationToken.None);

        Assert.Equal(expectedAsset, result.DownloadAction!.AssetName);
        Assert.Equal(expectedLabel, result.DownloadAction.Label);
    }

    [Theory]
    [InlineData(DeploymentType.SourceBuild)]
    [InlineData(DeploymentType.Unknown)]
    public async Task Source_and_unknown_deployments_do_not_offer_package_download(DeploymentType deploymentType)
    {
        var release = Release(
            "v0.1.3-beta",
            prerelease: true,
            assets: [Asset("v0.1.3-beta", "CoH-Analytics-0.1.3-beta-win-x64.msi")]);

        var result = await Create(Beta012, deploymentType, release).CheckAsync(CancellationToken.None);

        Assert.Null(result.DownloadAction);
        Assert.NotNull(result.ReleaseUrl);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("wrong-state")]
    [InlineData("zero-size")]
    [InlineData("wrong-host")]
    [InlineData("wrong-tag")]
    public async Task Unsafe_or_ambiguous_asset_is_not_offered(string condition)
    {
        const string name = "CoH-Analytics-0.1.3-beta-win-x64.msi";
        var valid = Asset("v0.1.3-beta", name);
        IReadOnlyList<GitHubReleaseAssetInfo> assets = condition switch
        {
            "missing" => [],
            "duplicate" => [valid, valid],
            "wrong-state" => [valid with { State = "new" }],
            "zero-size" => [valid with { Size = 0 }],
            "wrong-host" => [valid with { BrowserDownloadUrl = "https://example.com/file.msi" }],
            "wrong-tag" => [valid with
            {
                BrowserDownloadUrl = $"https://github.com/WITSLLC/CoH-Analytics/releases/download/v9.9.9/{name}"
            }],
            _ => throw new InvalidOperationException()
        };

        var result = await Create(
                Beta012,
                DeploymentType.WindowsInstaller,
                Release("v0.1.3-beta", prerelease: true, assets: assets))
            .CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Null(result.DownloadAction);
        Assert.NotNull(result.ReleaseUrl);
    }

    [Fact]
    public async Task Invalid_release_url_is_not_selected()
    {
        var invalid = Release("v9.0.0") with { HtmlUrl = "https://example.com/releases/v9.0.0" };
        var result = await Create(Beta012, DeploymentType.SourceBuild, invalid, Release("v0.1.2-beta", prerelease: true))
            .CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Current, result.Status);
    }

    [Fact]
    public async Task Client_failure_returns_unable_to_check()
    {
        var service = new UpdateCheckService(new ThrowingClient(), Beta012, DeploymentType.SourceBuild);

        var result = await service.CheckAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UnableToCheck, result.Status);
        Assert.Equal("http_error", result.FailureCode);
        Assert.Equal(503, result.HttpStatus);
    }

    private static UpdateCheckService Create(
        ReleaseVersion current,
        DeploymentType deploymentType,
        params GitHubReleaseInfo[] releases) =>
        new(new StubClient(releases), current, deploymentType);

    private static ReleaseVersion Parse(string value)
    {
        Assert.True(ReleaseVersion.TryParse(value, out var result));
        return result!;
    }

    private static GitHubReleaseInfo Release(
        string tag,
        bool draft = false,
        bool prerelease = false,
        bool published = true,
        IReadOnlyList<GitHubReleaseAssetInfo>? assets = null) =>
        new(
            tag,
            tag,
            draft,
            prerelease,
            published ? DateTimeOffset.Parse("2026-09-09T00:00:00Z") : null,
            $"https://github.com/WITSLLC/CoH-Analytics/releases/tag/{tag}",
            assets ?? []);

    private static GitHubReleaseAssetInfo Asset(string tag, string name) =>
        new(
            name,
            "uploaded",
            100,
            $"https://github.com/WITSLLC/CoH-Analytics/releases/download/{tag}/{name}");

    private sealed class StubClient(IReadOnlyList<GitHubReleaseInfo> releases) : IGitHubReleaseClient
    {
        public Task<IReadOnlyList<GitHubReleaseInfo>> GetReleasesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(releases);
    }

    private sealed class ThrowingClient : IGitHubReleaseClient
    {
        public Task<IReadOnlyList<GitHubReleaseInfo>> GetReleasesAsync(CancellationToken cancellationToken) =>
            throw new GitHubReleaseClientException("http_error", "Unavailable", 503);
    }
}

namespace CoHAnalytics.Updates;

public sealed class UpdateCheckService : IUpdateCheckService
{
    private const string RepositoryReleasePath = "/WITSLLC/CoH-Analytics/releases/tag/";
    private const string RepositoryDownloadPath = "/WITSLLC/CoH-Analytics/releases/download/";

    private readonly IGitHubReleaseClient _releaseClient;
    private readonly ReleaseVersion _currentVersion;
    private readonly DeploymentType _deploymentType;

    public UpdateCheckService(
        IGitHubReleaseClient releaseClient,
        ReleaseVersion currentVersion,
        DeploymentType deploymentType)
    {
        _releaseClient = releaseClient ?? throw new ArgumentNullException(nameof(releaseClient));
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        _deploymentType = deploymentType;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<GitHubReleaseInfo> releases;
        try
        {
            releases = await _releaseClient.GetReleasesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            return Unable("cancelled", exception: exception);
        }
        catch (GitHubReleaseClientException exception)
        {
            return Unable(exception.FailureCode, exception.HttpStatus, exception);
        }
        catch (Exception exception)
        {
            return Unable("unexpected_failure", exception: exception);
        }

        var candidates = releases
            .Select(TryCreateCandidate)
            .Where(candidate => candidate is not null)
            .Cast<ReleaseCandidate>()
            .OrderByDescending(candidate => candidate.Version)
            .ToArray();

        if (candidates.Length == 0)
        {
            return Unable("no_valid_releases", releaseCount: releases.Count);
        }

        var latest = candidates[0];
        if (latest.Version.CompareTo(_currentVersion) <= 0)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Current,
                CurrentVersion = _currentVersion,
                RemoteVersion = latest.Version,
                DeploymentType = _deploymentType,
                ReleaseCount = releases.Count
            };
        }

        return new UpdateCheckResult
        {
            Status = UpdateCheckStatus.UpdateAvailable,
            CurrentVersion = _currentVersion,
            RemoteVersion = latest.Version,
            DeploymentType = _deploymentType,
            ReleaseUrl = latest.ReleaseUrl,
            DownloadAction = ResolveDownloadAction(latest),
            ReleaseCount = releases.Count
        };
    }

    private ReleaseCandidate? TryCreateCandidate(GitHubReleaseInfo release)
    {
        if (release.Draft
            || release.PublishedAt is null
            || !ReleaseVersion.TryParse(release.TagName, out var version)
            || version is null
            || version.IsBeta != release.Prerelease
            || (!_currentVersion.IsBeta && version.IsBeta)
            || !TryValidateReleaseUrl(release.HtmlUrl, release.TagName, out var releaseUrl))
        {
            return null;
        }

        return new ReleaseCandidate(version, release.TagName, releaseUrl, release.Assets);
    }

    private UpdateDownloadAction? ResolveDownloadAction(ReleaseCandidate release)
    {
        var (assetName, label) = _deploymentType switch
        {
            DeploymentType.WindowsInstaller =>
                ($"CoH-Analytics-{release.Version.CanonicalText}-win-x64.msi", "Download Installer"),
            DeploymentType.PortableSelfContained =>
                ($"CoH-Analytics-{release.Version.CanonicalText}-win-x64-self-contained.zip", "Download Portable ZIP"),
            DeploymentType.PortableFrameworkDependent =>
                ($"CoH-Analytics-{release.Version.CanonicalText}-win-x64-framework-dependent.zip", "Download Framework-dependent ZIP"),
            _ => (null, null)
        };

        if (assetName is null || label is null)
        {
            return null;
        }

        var matches = release.Assets
            .Where(asset => string.Equals(asset.Name, assetName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            return null;
        }

        var match = matches[0];
        return match.Size > 0
               && string.Equals(match.State, "uploaded", StringComparison.Ordinal)
               && TryValidateAssetUrl(match.BrowserDownloadUrl, release.TagName, assetName, out var assetUrl)
            ? new UpdateDownloadAction(label, assetName, assetUrl)
            : null;
    }

    private UpdateCheckResult Unable(
        string failureCode,
        int? httpStatus = null,
        Exception? exception = null,
        int releaseCount = 0) =>
        new()
        {
            Status = UpdateCheckStatus.UnableToCheck,
            CurrentVersion = _currentVersion,
            DeploymentType = _deploymentType,
            ReleaseCount = releaseCount,
            FailureCode = failureCode,
            HttpStatus = httpStatus,
            ExceptionType = exception?.GetType().FullName ?? exception?.GetType().Name,
            HResult = exception?.HResult
        };

    internal static bool TryValidateReleaseUrl(string? value, string tagName, out string validatedUrl) =>
        TryValidateGitHubUrl(value, RepositoryReleasePath + tagName, out validatedUrl);

    internal static bool TryValidateAssetUrl(
        string? value,
        string tagName,
        string assetName,
        out string validatedUrl) =>
        TryValidateGitHubUrl(value, RepositoryDownloadPath + tagName + "/" + assetName, out validatedUrl);

    private static bool TryValidateGitHubUrl(string? value, string expectedPath, out string validatedUrl)
    {
        validatedUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.Equals(Uri.UnescapeDataString(uri.AbsolutePath), expectedPath, StringComparison.Ordinal))
        {
            return false;
        }

        validatedUrl = uri.AbsoluteUri;
        return true;
    }

    private sealed record ReleaseCandidate(
        ReleaseVersion Version,
        string TagName,
        string ReleaseUrl,
        IReadOnlyList<GitHubReleaseAssetInfo> Assets);
}

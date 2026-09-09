namespace CoHAnalytics.Updates;

public sealed record GitHubReleaseInfo(
    string TagName,
    string? Name,
    bool Draft,
    bool Prerelease,
    DateTimeOffset? PublishedAt,
    string? HtmlUrl,
    IReadOnlyList<GitHubReleaseAssetInfo> Assets);

public sealed record GitHubReleaseAssetInfo(
    string Name,
    string? State,
    long Size,
    string? BrowserDownloadUrl);

public interface IGitHubReleaseClient
{
    Task<IReadOnlyList<GitHubReleaseInfo>> GetReleasesAsync(CancellationToken cancellationToken);
}

public sealed class GitHubReleaseClientException : Exception
{
    public GitHubReleaseClientException(
        string failureCode,
        string message,
        int? httpStatus = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureCode = failureCode;
        HttpStatus = httpStatus;
    }

    public string FailureCode { get; }

    public int? HttpStatus { get; }
}

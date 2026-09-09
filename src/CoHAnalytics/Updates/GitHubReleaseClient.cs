using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoHAnalytics.Updates;

public sealed class GitHubReleaseClient : IGitHubReleaseClient, IDisposable
{
    internal const string ReleasesUri =
        "https://api.github.com/repos/WITSLLC/CoH-Analytics/releases?per_page=100";

    private const long MaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _userAgentVersion;

    public GitHubReleaseClient(string runningVersion)
        : this(CreateClient(), runningVersion, ownsHttpClient: true)
    {
    }

    public GitHubReleaseClient(HttpClient httpClient, string runningVersion, bool ownsHttpClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _userAgentVersion = string.IsNullOrWhiteSpace(runningVersion) ? "unknown" : runningVersion.Trim();
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<IReadOnlyList<GitHubReleaseInfo>> GetReleasesAsync(CancellationToken cancellationToken)
    {
        var releases = new List<GitHubReleaseInfo>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Uri? nextUri = new(ReleasesUri);

        while (nextUri is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsAllowedApiUri(nextUri) || !visited.Add(nextUri.AbsoluteUri))
            {
                throw new GitHubReleaseClientException("invalid_pagination", "GitHub returned an invalid pagination link.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, nextUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
            request.Headers.UserAgent.ParseAdd($"CoH-Analytics/{_userAgentVersion}");

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new GitHubReleaseClientException(
                    "request_failed",
                    "The GitHub release request failed.",
                    innerException: exception);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new GitHubReleaseClientException(
                        "http_error",
                        "GitHub returned an unsuccessful response.",
                        (int)response.StatusCode);
                }

                if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
                {
                    throw new GitHubReleaseClientException("response_too_large", "The GitHub response was too large.");
                }

                List<GitHubReleaseDto>? page;
                try
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    page = await JsonSerializer.DeserializeAsync<List<GitHubReleaseDto>>(
                        stream,
                        SerializerOptions,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException)
                {
                    throw new GitHubReleaseClientException(
                        "malformed_response",
                        "GitHub returned an unreadable release response.",
                        innerException: exception);
                }

                if (page is null)
                {
                    throw new GitHubReleaseClientException("malformed_response", "GitHub returned an empty release response.");
                }

                releases.AddRange(page.Select(ToModel));
                nextUri = GetNextUri(response.Headers);
            }
        }

        return releases;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static HttpClient CreateClient() => new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
        MaxResponseContentBufferSize = MaximumResponseBytes
    };

    private static GitHubReleaseInfo ToModel(GitHubReleaseDto dto) =>
        new(
            dto.TagName ?? string.Empty,
            dto.Name,
            dto.Draft,
            dto.Prerelease,
            dto.PublishedAt,
            dto.HtmlUrl,
            dto.Assets?.Select(asset => new GitHubReleaseAssetInfo(
                asset.Name ?? string.Empty,
                asset.State,
                asset.Size,
                asset.BrowserDownloadUrl)).ToArray() ?? []);

    private static Uri? GetNextUri(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            foreach (var segment in value.Split(','))
            {
                var parts = segment.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !parts.Skip(1).Any(part => part.Equals("rel=\"next\"", StringComparison.Ordinal)))
                {
                    continue;
                }

                var uriText = parts[0].Trim();
                if (uriText.Length > 2 && uriText[0] == '<' && uriText[^1] == '>')
                {
                    return Uri.TryCreate(uriText[1..^1], UriKind.Absolute, out var uri) ? uri : null;
                }
            }
        }

        return null;
    }

    private static bool IsAllowedApiUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.AbsolutePath, "/repos/WITSLLC/CoH-Analytics/releases", StringComparison.Ordinal)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment);

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAssetDto>? Assets { get; init; }
    }

    private sealed class GitHubReleaseAssetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("state")]
        public string? State { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }
    }
}

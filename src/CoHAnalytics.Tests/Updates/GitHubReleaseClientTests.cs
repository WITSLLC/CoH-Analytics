using System.Net;
using System.Net.Http;
using System.Text;
using CoHAnalytics.Updates;

namespace CoHAnalytics.Tests.Updates;

public sealed class GitHubReleaseClientTests
{
    [Fact]
    public async Task Request_uses_required_headers_and_maps_release_fields()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""
            [{
              "tag_name":"v0.1.3-beta",
              "name":"Beta",
              "draft":false,
              "prerelease":true,
              "published_at":"2026-09-09T00:00:00Z",
              "html_url":"https://github.com/WITSLLC/CoH-Analytics/releases/tag/v0.1.3-beta",
              "assets":[{
                "name":"CoH-Analytics-0.1.3-beta-win-x64.msi",
                "state":"uploaded",
                "size":123,
                "browser_download_url":"https://github.com/WITSLLC/CoH-Analytics/releases/download/v0.1.3-beta/CoH-Analytics-0.1.3-beta-win-x64.msi"
              }]
            }]
            """));
        using var httpClient = new HttpClient(handler);
        using var client = new GitHubReleaseClient(httpClient, "0.1.2-beta");

        var releases = await client.GetReleasesAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(GitHubReleaseClient.ReleasesUri, request.Uri);
        Assert.Equal("application/vnd.github+json", request.Accept);
        Assert.Equal("2026-03-10", request.ApiVersion);
        Assert.Contains("CoH-Analytics/0.1.2-beta", request.UserAgent);
        var release = Assert.Single(releases);
        Assert.Equal("v0.1.3-beta", release.TagName);
        Assert.Equal(123, Assert.Single(release.Assets).Size);
    }

    [Fact]
    public async Task Valid_next_link_is_followed_and_pages_are_combined()
    {
        var page = 0;
        var handler = new RecordingHandler(_ =>
        {
            page++;
            var response = JsonResponse(page == 1
                ? "[{\"tag_name\":\"v0.1.2-beta\",\"published_at\":\"2026-09-01T00:00:00Z\",\"html_url\":\"https://github.com/WITSLLC/CoH-Analytics/releases/tag/v0.1.2-beta\"}]"
                : "[{\"tag_name\":\"v0.1.3-beta\",\"published_at\":\"2026-09-09T00:00:00Z\",\"html_url\":\"https://github.com/WITSLLC/CoH-Analytics/releases/tag/v0.1.3-beta\"}]");
            if (page == 1)
            {
                response.Headers.TryAddWithoutValidation(
                    "Link",
                    "<https://api.github.com/repos/WITSLLC/CoH-Analytics/releases?per_page=100&page=2>; rel=\"next\"");
            }

            return response;
        });
        using var client = new GitHubReleaseClient(new HttpClient(handler), "test", ownsHttpClient: true);

        var releases = await client.GetReleasesAsync(CancellationToken.None);

        Assert.Equal(2, releases.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Cross_host_next_link_is_rejected()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = JsonResponse("[]");
            response.Headers.TryAddWithoutValidation("Link", "<https://example.com/releases?page=2>; rel=\"next\"");
            return response;
        });
        using var client = new GitHubReleaseClient(new HttpClient(handler), "test", ownsHttpClient: true);

        var error = await Assert.ThrowsAsync<GitHubReleaseClientException>(
            () => client.GetReleasesAsync(CancellationToken.None));

        Assert.Equal("invalid_pagination", error.FailureCode);
    }

    [Fact]
    public async Task Malformed_json_is_classified_without_leaking_content()
    {
        var handler = new RecordingHandler(_ => JsonResponse("not-json"));
        using var client = new GitHubReleaseClient(new HttpClient(handler), "test", ownsHttpClient: true);

        var error = await Assert.ThrowsAsync<GitHubReleaseClientException>(
            () => client.GetReleasesAsync(CancellationToken.None));

        Assert.Equal("malformed_response", error.FailureCode);
        Assert.DoesNotContain("not-json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_declared_response_is_rejected_before_reading()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = JsonResponse("[]");
            response.Content.Headers.ContentLength = 2 * 1024 * 1024 + 1;
            return response;
        });
        using var client = new GitHubReleaseClient(new HttpClient(handler), "test", ownsHttpClient: true);

        var error = await Assert.ThrowsAsync<GitHubReleaseClientException>(
            () => client.GetReleasesAsync(CancellationToken.None));

        Assert.Equal("response_too_large", error.FailureCode);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<RequestSnapshot> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RequestSnapshot(
                request.RequestUri!.AbsoluteUri,
                request.Headers.Accept.Single().MediaType,
                request.Headers.GetValues("X-GitHub-Api-Version").Single(),
                request.Headers.UserAgent.ToString()));
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed record RequestSnapshot(string Uri, string? Accept, string ApiVersion, string UserAgent);
}

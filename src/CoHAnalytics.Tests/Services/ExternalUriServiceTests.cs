using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class ExternalUriServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-uri")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    public void TryOpenUri_rejects_invalid_or_unsafe_uris_without_throwing(string uri)
    {
        var service = new WindowsExternalUriService();

        string? failureReason = null;
        var exception = Record.Exception(() =>
        {
            var opened = service.TryOpenUri(uri, out failureReason);
            Assert.False(opened);
        });

        Assert.Null(exception);
        Assert.False(string.IsNullOrWhiteSpace(failureReason));
    }

    [Fact]
    public void TryOpenUri_returns_false_when_process_launch_fails()
    {
        var service = new ThrowingExternalUriService();

        string? failureReason = null;
        var exception = Record.Exception(() =>
        {
            var opened = service.TryOpenUri("https://example.com", out failureReason);
            Assert.False(opened);
        });

        Assert.Null(exception);
        Assert.Contains("Unable to open the link", failureReason, StringComparison.Ordinal);
    }

    private sealed class ThrowingExternalUriService : IExternalUriService
    {
        public bool TryOpenUri(string uri, out string? failureReason)
        {
            try
            {
                throw new InvalidOperationException("Launch blocked.");
            }
            catch (Exception exception)
            {
                failureReason = $"Unable to open the link: {exception.Message}";
                return false;
            }
        }
    }
}

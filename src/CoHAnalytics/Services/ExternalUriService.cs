using System.Diagnostics;

namespace CoHAnalytics.Services;

public interface IExternalUriService
{
    bool TryOpenUri(string uri, out string? failureReason);
}

public sealed class WindowsExternalUriService : IExternalUriService
{
    public bool TryOpenUri(string uri, out string? failureReason)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(uri)
                || !Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri)
                || (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
            {
                failureReason = "The link is not valid.";
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = parsedUri.AbsoluteUri,
                UseShellExecute = true
            });
            failureReason = null;
            return true;
        }
        catch (Exception exception)
        {
            failureReason = $"Unable to open the link: {exception.Message}";
            return false;
        }
    }
}

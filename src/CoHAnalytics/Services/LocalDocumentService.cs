using System.Diagnostics;

namespace CoHAnalytics.Services;

public interface ILocalDocumentService
{
    bool TryOpenDocument(string path, out string? failureReason);
}

public sealed class WindowsLocalDocumentService : ILocalDocumentService
{
    public bool TryOpenDocument(string path, out string? failureReason)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                failureReason = "The document could not be found.";
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            failureReason = null;
            return true;
        }
        catch (Exception exception)
        {
            failureReason = $"Unable to open the document: {exception.Message}";
            return false;
        }
    }
}

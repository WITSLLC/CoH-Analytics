using System.Diagnostics;
using Microsoft.Win32;

namespace CoHAnalytics.Services;

public interface IFolderInteractionService
{
    string? SelectFolder(string title, string? initialDirectory = null);

    bool TryOpenFolder(string path, out string? failureReason);
}

public sealed class WindowsFolderInteractionService : IFolderInteractionService
{
    public string? SelectFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public bool TryOpenFolder(string path, out string? failureReason)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                failureReason = "The folder does not exist.";
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
            failureReason = $"Unable to open the folder: {exception.Message}";
            return false;
        }
    }
}

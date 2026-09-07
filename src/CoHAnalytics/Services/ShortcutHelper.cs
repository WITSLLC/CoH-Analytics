namespace CoHAnalytics.Services;

internal readonly record struct ShortcutDetails(
    string ShortcutPath,
    string? TargetPath,
    string? WorkingDirectory);

internal static class ShortcutHelper
{
    public static IEnumerable<ShortcutDetails> EnumerateDesktopShortcuts() =>
        EnumerateShortcutsRecursive(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))
            .Concat(EnumerateShortcutsRecursive(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)));

    public static IEnumerable<ShortcutDetails> EnumerateStartMenuShortcuts()
    {
        var userPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs");
        foreach (var shortcut in EnumerateShortcutsRecursive(userPrograms))
        {
            yield return shortcut;
        }

        var commonPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            "Programs");
        foreach (var shortcut in EnumerateShortcutsRecursive(commonPrograms))
        {
            yield return shortcut;
        }
    }

    public static IEnumerable<string> DeriveInstallRootCandidates(ShortcutDetails shortcut)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(shortcut.TargetPath))
        {
            if (HomecomingPathRules.TryDeriveInstallRootFromLauncherExecutable(shortcut.TargetPath, out var launcherRoot)
                && !string.IsNullOrWhiteSpace(launcherRoot)
                && seen.Add(launcherRoot))
            {
                yield return launcherRoot;
            }

            if (HomecomingPathRules.TryDeriveInstallRootFromClientExecutable(shortcut.TargetPath, out var clientRoot)
                && !string.IsNullOrWhiteSpace(clientRoot)
                && seen.Add(clientRoot))
            {
                yield return clientRoot;
            }
        }

        if (!string.IsNullOrWhiteSpace(shortcut.WorkingDirectory)
            && seen.Add(shortcut.WorkingDirectory))
        {
            yield return shortcut.WorkingDirectory;
        }
    }

    private static IEnumerable<ShortcutDetails> EnumerateShortcutsRecursive(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            yield break;
        }

        IEnumerable<string> shortcutPaths;
        try
        {
            shortcutPaths = Directory
                .EnumerateFiles(directory, "*.lnk", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            yield break;
        }

        foreach (var shortcutPath in shortcutPaths)
        {
            var details = TryReadShortcut(shortcutPath);
            if (details is not null)
            {
                yield return details.Value;
            }
        }
    }

    private static ShortcutDetails? TryReadShortcut(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            string? targetPath = shortcut.TargetPath;
            string? workingDirectory = shortcut.WorkingDirectory;

            if (string.IsNullOrWhiteSpace(targetPath) && string.IsNullOrWhiteSpace(workingDirectory))
            {
                return null;
            }

            return new ShortcutDetails(
                shortcutPath,
                string.IsNullOrWhiteSpace(targetPath) ? null : targetPath,
                string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory);
        }
        catch
        {
            return null;
        }
    }
}

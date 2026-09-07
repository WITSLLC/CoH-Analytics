namespace CoHAnalytics.Services;

internal static class HomecomingPathRules
{
    public const string OfficialDefaultInstallRoot = @"C:\Games\Homecoming";

    private static readonly string[] KnownClientProfiles = ["live", "beta", "pre", "diag"];

    private static readonly string[] KnownClientRelativePaths =
    [
        @"bin\win64\live\cityofheroes.exe",
        @"bin\win32\live\cityofheroes.exe",
        @"bin\win64\beta\cityofheroes.exe",
        @"bin\win32\beta\cityofheroes.exe",
        @"bin\win64\pre\cityofheroes.exe",
        @"bin\win32\pre\cityofheroes.exe",
        @"bin\win64\diag\cityofheroes.exe"
    ];

    private static readonly string[] CommonInstallRoots =
    [
        @"D:\Games\Homecoming",
        @"E:\Games\Homecoming",
        @"C:\Program Files\Homecoming",
        @"C:\Program Files (x86)\Homecoming"
    ];

    public static IReadOnlyList<string> CommonInstallationCandidates => CommonInstallRoots;

    public static bool IsValidInstallRoot(string? installRoot) =>
        TryValidateInstallRoot(installRoot, out _, out _);

    public static bool TryValidateInstallRoot(string? installRoot, out string normalizedRoot, out string? failureReason)
    {
        normalizedRoot = string.Empty;
        failureReason = null;

        if (string.IsNullOrWhiteSpace(installRoot))
        {
            failureReason = "Path is empty.";
            return false;
        }

        try
        {
            normalizedRoot = NormalizeDirectory(installRoot);
        }
        catch
        {
            failureReason = "Path is invalid.";
            return false;
        }

        var launcherDirectory = Path.Combine(normalizedRoot, "settings", "launcher");
        if (!Directory.Exists(launcherDirectory))
        {
            failureReason = "Missing settings\\launcher directory.";
            return false;
        }

        if (!TryResolveLauncherPath(normalizedRoot, out _))
        {
            failureReason = "Missing bin\\win64\\launcher.exe and bin\\win32\\launcher.exe.";
            return false;
        }

        return true;
    }

    public static bool TryResolveLauncherPath(string installRoot, out string launcherPath)
    {
        launcherPath = string.Empty;
        var normalizedRoot = NormalizeDirectory(installRoot);
        var win64Launcher = Path.Combine(normalizedRoot, "bin", "win64", "launcher.exe");
        if (File.Exists(win64Launcher))
        {
            launcherPath = win64Launcher;
            return true;
        }

        var win32Launcher = Path.Combine(normalizedRoot, "bin", "win32", "launcher.exe");
        if (File.Exists(win32Launcher))
        {
            launcherPath = win32Launcher;
            return true;
        }

        return false;
    }

    public static string ResolveLauncherPath(string installRoot, string? launcherOverride)
    {
        if (!string.IsNullOrWhiteSpace(launcherOverride) && File.Exists(launcherOverride))
        {
            return Path.GetFullPath(launcherOverride);
        }

        if (!TryResolveLauncherPath(installRoot, out var launcherPath))
        {
            throw new FileNotFoundException("A Homecoming launcher executable could not be resolved.", installRoot);
        }

        return launcherPath;
    }

    public static bool TryDeriveInstallRootFromLauncherExecutable(string? launcherPath, out string? installRoot)
    {
        installRoot = null;

        if (string.IsNullOrWhiteSpace(launcherPath))
        {
            return false;
        }

        try
        {
            var fullPath = NormalizeFile(launcherPath);
            if (!fullPath.EndsWith("launcher.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var launcherDirectory = Directory.GetParent(fullPath)?.FullName;
            var architectureDirectory = launcherDirectory is null ? null : Directory.GetParent(launcherDirectory)?.FullName;
            var binDirectory = architectureDirectory is null ? null : Directory.GetParent(architectureDirectory)?.FullName;
            if (binDirectory is null || !string.Equals(Path.GetFileName(binDirectory), "bin", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var architectureName = Path.GetFileName(architectureDirectory!);
            if (!string.Equals(architectureName, "win64", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(architectureName, "win32", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            installRoot = Directory.GetParent(binDirectory)?.FullName;
            return !string.IsNullOrWhiteSpace(installRoot);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDeriveInstallRootFromClientExecutable(string? clientPath, out string? installRoot)
    {
        installRoot = null;

        if (string.IsNullOrWhiteSpace(clientPath))
        {
            return false;
        }

        try
        {
            var fullPath = NormalizeFile(clientPath);
            if (!fullPath.EndsWith("cityofheroes.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var profileDirectory = Directory.GetParent(fullPath)?.FullName;
            var architectureDirectory = profileDirectory is null ? null : Directory.GetParent(profileDirectory)?.FullName;
            var binDirectory = architectureDirectory is null ? null : Directory.GetParent(architectureDirectory)?.FullName;
            if (binDirectory is null || !string.Equals(Path.GetFileName(binDirectory), "bin", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var profileName = Path.GetFileName(profileDirectory!);
            if (!KnownClientProfiles.Contains(profileName, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            var architectureName = Path.GetFileName(architectureDirectory!);
            if (!string.Equals(architectureName, "win64", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(architectureName, "win32", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            installRoot = Directory.GetParent(binDirectory)?.FullName;
            return !string.IsNullOrWhiteSpace(installRoot);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsKnownClientExecutable(string installRoot, string executablePath)
    {
        var normalizedRoot = NormalizeDirectory(installRoot);
        var normalizedExecutable = NormalizeFile(executablePath);

        if (!normalizedExecutable.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = normalizedExecutable[normalizedRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return KnownClientRelativePaths.Any(candidate =>
            string.Equals(
                relative.Replace('/', '\\'),
                candidate,
                StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public static string NormalizeFile(string path) =>
        Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}

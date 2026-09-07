using System.Diagnostics;

namespace CoHAnalytics.Services;

internal static class MidsPathRules
{
    public const string ExecutableFileName = "MidsReborn.exe";
    public const string MainAssemblyFileName = "MidsReborn.dll";
    public const string HomecomingDatabaseFolderName = "Homecoming";
    public const string MainDatabaseFileName = "I12.mhd";
    public const string EnhancementDatabaseFileName = "EnhDB.mhd";
    public const string SalvageDatabaseFileName = "Salvage.mhd";
    public const string RecipeDatabaseFileName = "Recipe.mhd";
    public const string DatabasesFolderName = "Databases";
    private const string MainDatabaseHeader = "Mids Reborn Powers Database";

    public static string OfficialDefaultInstallRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LoadedCamel",
            "MidsReborn");

    public static IReadOnlyList<string> CommonInstallationCandidates => [];

    public static bool TryValidateInstallRoot(string? installRoot, out MidsValidatedInstallation validated, out string? failureReason)
    {
        validated = default;
        failureReason = null;

        if (string.IsNullOrWhiteSpace(installRoot))
        {
            failureReason = "Path is empty.";
            return false;
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = NormalizeDirectory(installRoot);
        }
        catch
        {
            failureReason = "Path is invalid.";
            return false;
        }

        if (!TryResolveExecutablePath(normalizedRoot, out var executablePath, out var executableFailure))
        {
            failureReason = executableFailure;
            return false;
        }

        var mainAssemblyPath = Path.Combine(normalizedRoot, MainAssemblyFileName);
        if (!File.Exists(mainAssemblyPath))
        {
            failureReason = $"Missing {MainAssemblyFileName}.";
            return false;
        }

        var databasesDirectory = Path.Combine(normalizedRoot, DatabasesFolderName);
        if (!Directory.Exists(databasesDirectory))
        {
            failureReason = $"Missing {DatabasesFolderName} directory.";
            return false;
        }

        var homecomingDatabasePath = Path.Combine(databasesDirectory, HomecomingDatabaseFolderName);
        if (!Directory.Exists(homecomingDatabasePath))
        {
            failureReason = $"Missing {DatabasesFolderName}\\{HomecomingDatabaseFolderName} directory.";
            return false;
        }

        var mainDatabasePath = Path.Combine(homecomingDatabasePath, MainDatabaseFileName);
        if (!File.Exists(mainDatabasePath))
        {
            failureReason = $"Missing {DatabasesFolderName}\\{HomecomingDatabaseFolderName}\\{MainDatabaseFileName}.";
            return false;
        }

        var enhancementDatabasePath = Path.Combine(homecomingDatabasePath, EnhancementDatabaseFileName);
        if (!File.Exists(enhancementDatabasePath))
        {
            failureReason = $"Missing {DatabasesFolderName}\\{HomecomingDatabaseFolderName}\\{EnhancementDatabaseFileName}.";
            return false;
        }

        if (!TryReadHomecomingDatabaseVersion(homecomingDatabasePath, out var homecomingDatabaseVersion, out var databaseFailure))
        {
            failureReason = databaseFailure;
            return false;
        }

        validated = new MidsValidatedInstallation(
            normalizedRoot,
            executablePath,
            TryReadApplicationVersion(executablePath),
            homecomingDatabasePath,
            homecomingDatabaseVersion);

        return true;
    }

    public static bool TryResolveExecutablePath(string installRoot, out string executablePath, out string? failureReason)
    {
        executablePath = string.Empty;
        failureReason = null;

        var normalizedRoot = NormalizeDirectory(installRoot);
        var candidate = Path.Combine(normalizedRoot, ExecutableFileName);
        if (File.Exists(candidate))
        {
            executablePath = candidate;
            return true;
        }

        failureReason = $"Missing {ExecutableFileName}.";
        return false;
    }

    public static string ResolveExecutablePath(string installRoot, string? executableOverride)
    {
        if (!string.IsNullOrWhiteSpace(executableOverride) && File.Exists(executableOverride))
        {
            return Path.GetFullPath(executableOverride);
        }

        if (!TryResolveExecutablePath(installRoot, out var executablePath, out var failureReason))
        {
            throw new FileNotFoundException(failureReason ?? "A Mids Reborn executable could not be resolved.", installRoot);
        }

        return executablePath;
    }

    public static bool TryDeriveInstallRootFromExecutable(string? executablePath, out string? installRoot)
    {
        installRoot = null;

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            var fullPath = NormalizeFile(executablePath);
            var fileName = Path.GetFileName(fullPath);
            if (!string.Equals(fileName, ExecutableFileName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(fileName, "MRBBootstrap.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            installRoot = Directory.GetParent(fullPath)?.FullName;
            return !string.IsNullOrWhiteSpace(installRoot);
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<string> DeriveInstallRootCandidates(ShortcutDetails shortcut)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(shortcut.TargetPath)
            && TryDeriveInstallRootFromExecutable(shortcut.TargetPath, out var targetRoot)
            && !string.IsNullOrWhiteSpace(targetRoot)
            && seen.Add(targetRoot))
        {
            yield return targetRoot;
        }

        if (!string.IsNullOrWhiteSpace(shortcut.WorkingDirectory)
            && seen.Add(shortcut.WorkingDirectory))
        {
            yield return shortcut.WorkingDirectory;
        }
    }

    public static string? TryReadApplicationVersion(string executablePath)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            if (!string.IsNullOrWhiteSpace(versionInfo.ProductVersion))
            {
                return versionInfo.ProductVersion.Trim();
            }

            if (!string.IsNullOrWhiteSpace(versionInfo.FileVersion))
            {
                return versionInfo.FileVersion.Trim();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static bool TryReadHomecomingDatabaseVersion(
        string homecomingDatabasePath,
        out string? version,
        out string? failureReason)
    {
        version = null;
        failureReason = null;

        var mainDatabasePath = Path.Combine(homecomingDatabasePath, MainDatabaseFileName);
        if (!File.Exists(mainDatabasePath))
        {
            failureReason = $"Missing {MainDatabaseFileName}.";
            return false;
        }

        try
        {
            using var fileStream = new FileStream(mainDatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(fileStream);
            var header = reader.ReadString();
            if (!string.Equals(header, MainDatabaseHeader, StringComparison.Ordinal))
            {
                failureReason = "Homecoming database header is invalid.";
                return false;
            }

            var rawVersion = reader.ReadString();
            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                failureReason = "Homecoming database version is missing.";
                return false;
            }

            version = rawVersion.Trim();
            return true;
        }
        catch (Exception ex)
        {
            failureReason = $"Unable to read Homecoming database version: {ex.Message}";
            return false;
        }
    }

    public static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public static string NormalizeFile(string path) =>
        Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}

internal readonly record struct MidsValidatedInstallation(
    string InstallRoot,
    string ExecutablePath,
    string? ApplicationVersion,
    string HomecomingDatabasePath,
    string? HomecomingDatabaseVersion);

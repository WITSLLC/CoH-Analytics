using Microsoft.Win32;

namespace CoHAnalytics.Services;

internal static class HomecomingRegistryHintHelper
{
    private static readonly string[] UninstallKeyPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public static IEnumerable<string> EnumerateInstallHints()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hint in EnumerateHive(Registry.CurrentUser, UninstallKeyPaths[0]))
        {
            if (seen.Add(hint))
            {
                yield return hint;
            }
        }

        foreach (var keyPath in UninstallKeyPaths)
        {
            foreach (var hint in EnumerateHive(Registry.LocalMachine, keyPath))
            {
                if (seen.Add(hint))
                {
                    yield return hint;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateHive(RegistryKey hive, string keyPath)
    {
        using var uninstallKey = hive.OpenSubKey(keyPath);
        if (uninstallKey is null)
        {
            yield break;
        }

        foreach (var subKeyName in uninstallKey.GetSubKeyNames())
        {
            using var subKey = uninstallKey.OpenSubKey(subKeyName);
            if (subKey is null)
            {
                continue;
            }

            var displayName = subKey.GetValue("DisplayName") as string;
            if (!LooksLikeHomecomingEntry(displayName))
            {
                continue;
            }

            foreach (var hint in ExtractPathHints(subKey))
            {
                yield return hint;
            }
        }
    }

    private static bool LooksLikeHomecomingEntry(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        return displayName.Contains("homecoming", StringComparison.OrdinalIgnoreCase)
               || displayName.Contains("city of heroes", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ExtractPathHints(RegistryKey subKey)
    {
        foreach (var valueName in new[] { "InstallLocation", "DisplayIcon", "UninstallString" })
        {
            var rawValue = subKey.GetValue(valueName) as string;
            var hint = ExtractDirectoryHint(rawValue);
            if (!string.IsNullOrWhiteSpace(hint))
            {
                yield return hint;
            }
        }
    }

    private static string? ExtractDirectoryHint(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var trimmed = rawValue.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Contains(',', StringComparison.Ordinal))
        {
            trimmed = trimmed.Split(',')[0].Trim().Trim('"');
        }

        if (trimmed.Contains(' '))
        {
            var firstToken = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].Trim('"');
            if (firstToken.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = firstToken;
            }
        }

        try
        {
            if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return Directory.Exists(trimmed) ? trimmed : Path.GetDirectoryName(trimmed);
            }

            return Directory.Exists(trimmed) ? trimmed : null;
        }
        catch
        {
            return null;
        }
    }
}
